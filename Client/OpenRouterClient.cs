using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace IdeorAI.Client;

public sealed class OpenRouterClient(
    IHttpClientFactory httpClientFactory,
    IEnumerable<string> models,
    ILogger<OpenRouterClient> logger) : ILlmClient
{
    private readonly IReadOnlyList<string> _models = models.ToList() is { Count: > 0 } m
        ? m.AsReadOnly()
        : throw new ArgumentException("Pelo menos um modelo OpenRouter deve ser configurado.");

    public string ProviderName => "openrouter";
    public int Priority => 2;

    public async Task<LlmResult> GenerateAsync(string prompt, LlmOptions? options = null, CancellationToken ct = default)
    {
        var opts = options ?? new LlmOptions();
        const int maxRounds = 2;
        var sw = Stopwatch.StartNew();

        for (int round = 0; round < maxRounds; round++)
        {
            for (int i = 0; i < _models.Count; i++)
            {
                var model = _models[i];
                logger.LogInformation("[OpenRouter] Tentando modelo {Model} (rodada {Round}, slot {Slot})",
                    model, round + 1, i + 1);

                var client = httpClientFactory.CreateClient("OpenRouter");

                var request = new
                {
                    model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = opts.Temperature,
                    // Modelos free no OpenRouter têm limites de saída menores; cap em 4000
                    max_tokens = Math.Min(opts.MaxTokens, 4000)
                };

                HttpResponseMessage response;
                try
                {
                    response = await client.PostAsJsonAsync(
                        "https://openrouter.ai/api/v1/chat/completions", request, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[OpenRouter] Erro de rede no modelo {Model}, tentando próximo", model);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("[OpenRouter] Rate limit (429) no modelo {Model}, tentando próximo", model);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    logger.LogError("[OpenRouter] Erro {StatusCode} no modelo {Model}: {Body}",
                        response.StatusCode, model, errorBody[..Math.Min(300, errorBody.Length)]);
                    continue;
                }

                string responseBody;
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[OpenRouter] Falha ao ler body do modelo {Model}", model);
                    continue;
                }

                JsonDocument json;
                try
                {
                    json = JsonDocument.Parse(responseBody);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[OpenRouter] Body não é JSON válido no modelo {Model}: {Preview}",
                        model, responseBody[..Math.Min(200, responseBody.Length)]);
                    continue;
                }

                var root = json.RootElement;

                string content;
                int inputTokens = 0, outputTokens = 0;
                try
                {
                    var choices = root.GetProperty("choices");
                    if (choices.GetArrayLength() == 0)
                    {
                        logger.LogWarning("[OpenRouter] Modelo {Model} retornou choices vazio", model);
                        continue;
                    }

                    var contentEl = choices[0].GetProperty("message").GetProperty("content");
                    var raw = LlmResponseParser.ExtractContent(contentEl);
                    content = LlmResponseParser.StripCodeFences(raw);

                    if (root.TryGetProperty("usage", out var usage))
                    {
                        if (usage.TryGetProperty("prompt_tokens",     out var pt))  inputTokens  = pt.GetInt32();
                        if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[OpenRouter] Estrutura de resposta inesperada no modelo {Model}: {Preview}",
                        model, responseBody[..Math.Min(200, responseBody.Length)]);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    logger.LogWarning("[OpenRouter] Modelo {Model} retornou conteúdo vazio", model);
                    continue;
                }

                sw.Stop();
                logger.LogInformation("[OpenRouter] ✅ {Model} — {In}t in, {Out}t out, {Ms}ms",
                    model, inputTokens, outputTokens, sw.ElapsedMilliseconds);

                return new LlmResult(content, inputTokens, outputTokens, model, ProviderName, sw.ElapsedMilliseconds);
            }

            if (round < maxRounds - 1)
            {
                logger.LogWarning("[OpenRouter] Todos os {Count} modelo(s) falharam (rodada {Round}). Aguardando 3s...",
                    _models.Count, round + 1);
                await Task.Delay(3_000, ct);
            }
        }

        throw new InvalidOperationException(
            $"[OpenRouter] Todos os modelos ({string.Join(", ", _models)}) falharam após {maxRounds} rodadas.");
    }

    // Backward-compat: usado por DocumentGenerationService e BusinessIdeasController até Phase 5
    public async Task<GeminiResult> GenerateContentWithMetadataAsync(string prompt, CancellationToken ct = default)
    {
        var result = await GenerateAsync(prompt, null, ct);
        return new GeminiResult(result.Text, result.InputTokens, result.OutputTokens, result.ModelName);
    }

    public Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default)
        => GenerateContentWithMetadataAsync(prompt, ct).ContinueWith(t => t.Result.Text, ct);
}
