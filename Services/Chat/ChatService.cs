using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using IdeorAI.Model.DTOs;
using IdeorAI.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IdeorAI.Services.Chat;

public sealed class ChatService(
    IHttpClientFactory httpClientFactory,
    IOptions<DeepSeekOptions> deepSeekOptions,
    IMemoryCache cache,
    ILogger<ChatService> logger) : IChatService
{
    private static readonly string[] StageNames =
        ["Início", "Problema", "Pesquisa", "Proposta de Valor", "Modelo de Negócio", "MVP"];

    private const int RateLimitPerHour = 20;

    public bool IsRateLimited(string userId)
    {
        var key = $"chat_rl_{userId}_{DateTime.UtcNow:yyyyMMddHH}";
        var count = cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60 - DateTime.UtcNow.Minute);
            return 0;
        });
        return count >= RateLimitPerHour;
    }

    private void IncrementRateLimit(string userId)
    {
        var key = $"chat_rl_{userId}_{DateTime.UtcNow:yyyyMMddHH}";
        cache.TryGetValue<int>(key, out var count);
        cache.Set(key, count + 1,
            DateTimeOffset.UtcNow.AddMinutes(60 - DateTime.UtcNow.Minute));
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        string userId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        IncrementRateLimit(userId);

        var opts = deepSeekOptions.Value;

        // RAG só é necessário no modo guia
        var ragContext = request.Mode != "refine"
            ? string.Join("\n\n---\n\n", RagKnowledgeBase.Retrieve(request.Message))
            : string.Empty;

        var stageName = request.StageName
            ?? (request.CurrentStageIndex >= 0 && request.CurrentStageIndex < StageNames.Length
                ? StageNames[request.CurrentStageIndex]
                : "desconhecida");

        string systemPrompt;

        if (request.Mode == "refine" && !string.IsNullOrWhiteSpace(request.StageContent))
        {
            systemPrompt = $"""
                Você é um especialista em validação de startups. O usuário quer refinar o conteúdo da etapa "{stageName}" do projeto "{request.ProjectName ?? "não informado"}".

                ## Conteúdo atual da etapa
                {request.StageContent}

                ## Instruções
                - Aplique as melhorias solicitadas pelo usuário mantendo o estilo e estrutura do conteúdo original
                - Retorne APENAS o conteúdo completo reescrito com as melhorias, sem explicações adicionais, sem prefácio
                - Responda em português do Brasil
                - Mantenha a mesma extensão aproximada do original, a menos que o usuário peça para expandir ou resumir
                """;
        }
        else
        {
            systemPrompt = $"""
                Você é o Guia IdeorAI, um assistente especializado em validação de startups.
                Sua função é orientar empreendedores na jornada de validação da plataforma IdeorAI.
                Responda sempre em português do Brasil, com tom encorajador e profissional.
                Seja conciso: máximo 200 palavras por resposta, a menos que o usuário peça mais detalhes.
                Não invente dados sobre o projeto do usuário. Não forneça conselhos jurídicos ou financeiros formais.
                Se não souber, diga "Não tenho essa informação no momento".

                ## Contexto do projeto atual
                - Nome: {request.ProjectName ?? "não informado"}
                - Etapa atual: {request.CurrentStageIndex + 1} de 6 — {stageName}
                - IVO Index: {(request.IvoScore.HasValue ? $"{request.IvoScore:F0}/100" : "ainda não calculado")}
                - Score: {(request.Score.HasValue ? $"{request.Score:F0}/100" : "ainda não calculado")}
                - Status Go/Pivot: {request.GoPivotVerdict ?? "ainda não avaliado"}

                ## Documentação relevante
                {ragContext}
                """;
        }

        var history = request.History
            .TakeLast(6)
            .Select(m => new { role = m.Role, content = m.Content })
            .ToList<object>();

        history.Insert(0, new { role = "system", content = systemPrompt });
        history.Add(new { role = "user", content = request.Message });

        var payload = new
        {
            model = opts.Model,
            messages = history,
            temperature = opts.Temperature,
            max_tokens = opts.MaxTokens,
            stream = true
        };

        var client = httpClientFactory.CreateClient("DeepSeek");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            "https://api.deepseek.com/v1/chat/completions");
        httpRequest.Content = JsonContent.Create(payload);

        HttpResponseMessage? response = null;
        string? connectError = null;
        try
        {
            response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[ChatService] Falha ao conectar na DeepSeek API");
            connectError = "[ERRO] Não foi possível conectar ao assistente. Tente novamente.";
        }

        if (connectError != null) { yield return connectError; yield break; }

        // response não pode ser `using var` antes do try, então garantimos dispose explícito
        try
        {
            if (!response!.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[ChatService] DeepSeek retornou {Status}: {Err}", response.StatusCode,
                    err[..Math.Min(200, err.Length)]);
                yield return "[ERRO] O assistente está temporariamente indisponível.";
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line[6..]; // skip "data: "
                if (data == "[DONE]") break;

                string? delta = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0) continue;
                    var deltaEl = choices[0].GetProperty("delta");
                    if (deltaEl.TryGetProperty("content", out var contentEl))
                        delta = contentEl.GetString();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(delta))
                    yield return delta;
            }
        }
        finally
        {
            response?.Dispose();
        }

        logger.LogDebug("[ChatService] Stream concluído para user {UserId}", userId);
    }
}
