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
    IOptions<ChatOptions> chatOptions,
    IMemoryCache cache,
    Supabase.Client supabase,
    ILogger<ChatService> logger) : IChatService
{
    private static readonly string[] StageNames =
        ["Início", "Problema", "Pesquisa", "Proposta de Valor", "Modelo de Negócio", "MVP"];

    private int RateLimitPerHour => chatOptions.Value.RateLimitPerHour;

    // Lock global para tornar check+increment do rate-limit atômico (corrige race condition).
    private static readonly object _rateLimitLock = new();

    public bool IsRateLimited(string userId)
    {
        var key = $"chat_rl_{userId}_{DateTime.UtcNow:yyyyMMddHH}";
        lock (_rateLimitLock)
        {
            cache.TryGetValue<int>(key, out var count);
            return count >= RateLimitPerHour;
        }
    }

    /// <summary>
    /// Check + increment atômico do rate limit. Retorna false se o usuário estourou o limite.
    /// </summary>
    public bool TryConsumeRateLimit(string userId)
    {
        var now = DateTime.UtcNow;
        var key = $"chat_rl_{userId}_{now:yyyyMMddHH}";
        var expiration = DateTimeOffset.UtcNow.AddMinutes(60 - now.Minute);

        lock (_rateLimitLock)
        {
            cache.TryGetValue<int>(key, out var count);
            if (count >= RateLimitPerHour) return false;
            cache.Set(key, count + 1, expiration);
            return true;
        }
    }

    // Mantido para retrocompatibilidade da interface, mas o uso recomendado é TryConsumeRateLimit.
    private void IncrementRateLimit(string userId)
    {
        var now = DateTime.UtcNow;
        var key = $"chat_rl_{userId}_{now:yyyyMMddHH}";
        var expiration = DateTimeOffset.UtcNow.AddMinutes(60 - now.Minute);
        lock (_rateLimitLock)
        {
            cache.TryGetValue<int>(key, out var count);
            cache.Set(key, count + 1, expiration);
        }
    }

    // PII-safe placeholder for logs (não loga conteúdo cru da DeepSeek com prompts do usuário).
    private static string SanitizeForLog(string s) =>
        string.IsNullOrEmpty(s) ? "[empty]" : $"[{s.Length} chars]";

    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        string userId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Rate limit já consumido atomicamente em ChatController via TryConsumeRateLimit.
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
            systemPrompt =
                $"Você é um especialista em validação de startups. O usuário quer refinar o conteúdo da etapa \"{stageName}\" do projeto \"{request.ProjectName ?? "não informado"}\".\n\n" +
                $"## Conteúdo atual da etapa (JSON)\n{request.StageContent}\n\n" +
                "## Instruções\n" +
                "- Aplique APENAS as melhorias solicitadas pelo usuário nas seções relevantes\n" +
                "- Retorne SOMENTE um JSON válido no formato abaixo, sem explicações, sem prefácio, sem markdown:\n" +
                "  {\"changed_sections\": {\"chave\": \"novo conteúdo refinado\"}}\n" +
                "- Inclua SOMENTE as chaves das seções que foram efetivamente modificadas. Não inclua seções não alteradas.\n" +
                "- Os valores devem ser strings com o conteúdo refinado em português do Brasil\n" +
                "- Não retorne o documento completo. Não adicione texto fora do JSON.";
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
            opts.BaseUrl + "/v1/chat/completions");
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

        bool isRefineMode = request.Mode == "refine" && !string.IsNullOrWhiteSpace(request.StageContent);
        var accumulator = isRefineMode ? new StringBuilder() : null;
        var deltaQueue = isRefineMode ? null : new List<string>();

        // Results collected inside try/finally (yield not allowed inside try/catch)
        string? pendingYield = null;
        string? earlyError = null;

        // response não pode ser `using var` antes do try, então garantimos dispose explícito
        try
        {
            if (!response!.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[ChatService] DeepSeek retornou {Status} (errSize={Size})",
                    response.StatusCode, SanitizeForLog(err));
                logger.LogDebug("[ChatService] DeepSeek error body: {Err}",
                    err[..Math.Min(200, err.Length)]);
                earlyError = "[ERRO] O assistente está temporariamente indisponível.";
            }
            else
            {
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
                    {
                        if (isRefineMode)
                            accumulator!.Append(delta);
                        else
                            deltaQueue!.Add(delta);
                    }
                }

                if (isRefineMode && accumulator != null)
                {
                    var fullResponse = accumulator.ToString().Trim();
                    // Remove markdown code fences if present
                    if (fullResponse.StartsWith("```"))
                    {
                        var firstNewline = fullResponse.IndexOf('\n');
                        if (firstNewline >= 0)
                            fullResponse = fullResponse[(firstNewline + 1)..];
                        if (fullResponse.EndsWith("```"))
                            fullResponse = fullResponse[..^3].TrimEnd();
                    }

                    try
                    {
                        using var diffDoc = JsonDocument.Parse(fullResponse);
                        if (!diffDoc.RootElement.TryGetProperty("changed_sections", out _))
                            throw new JsonException("Propriedade 'changed_sections' ausente");
                        pendingYield = $"\x02DIFF\x02{fullResponse}";
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "[ChatService] Resposta de refine não é JSON válido com changed_sections");
                        pendingYield = "\x02ERROR422\x02Não foi possível processar o refinamento: a resposta do assistente não está no formato esperado. Tente novamente.";
                    }
                }
            }
        }
        finally
        {
            response?.Dispose();
        }

        if (earlyError != null) { yield return earlyError; yield break; }

        if (deltaQueue != null)
            foreach (var d in deltaQueue)
                yield return d;

        if (pendingYield != null) yield return pendingYield;

        logger.LogDebug("[ChatService] Stream concluído para user {UserId}", userId);
    }

    /// <summary>
    /// Helper privado: monta o payload, faz POST não-streaming para a DeepSeek e
    /// retorna o content textual da primeira choice. Centraliza error handling e logging.
    /// </summary>
    private async Task<(string? Content, int? StatusCode, string? ErrorBody)> CallDeepSeekAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var opts = deepSeekOptions.Value;
        var payload = new
        {
            model = opts.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            temperature = opts.Temperature,
            max_tokens = opts.MaxTokens,
            stream = false
        };

        var client = httpClientFactory.CreateClient("DeepSeek");
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(opts.BaseUrl + "/v1/chat/completions", payload, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[ChatService] Falha ao conectar na DeepSeek API");
            return (null, null, "connection_error");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                logger.LogWarning("[ChatService] DeepSeek retornou {Status} (errSize={Size})",
                    response.StatusCode, SanitizeForLog(err));
                logger.LogDebug("[ChatService] DeepSeek error body: {Err}",
                    err[..Math.Min(200, err.Length)]);
                return (null, (int)response.StatusCode, err);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                return (content, 200, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ChatService] Falha ao parsear response da DeepSeek");
                return (null, null, body);
            }
        }
    }

    public async Task<(Dictionary<string, string>? Sections, string? ErrorRaw)> RefineDocumentAsync(
        RefineRequest request, string userId, CancellationToken ct)
    {
        // Rate limit já consumido atomicamente em ChatController via TryConsumeRateLimit.
        var systemPrompt =
            "Você é um especialista em validação de startups.\n" +
            "RESPONDA SOMENTE COM JSON VÁLIDO. Nenhum texto antes ou depois.\n\n" +
            $"Documento atual da etapa \"{request.StageName}\" (JSON):\n{request.StageContent}\n\n" +
            "Formato obrigatório da resposta:\n" +
            "{\"changed_sections\":{\"nome_da_chave\":\"conteúdo refinado completo\"}}\n\n" +
            "Regras:\n" +
            "- Inclua SOMENTE as chaves que foram alteradas\n" +
            "- NÃO inclua chaves não modificadas\n" +
            "- NÃO adicione texto, markdown, explicações ou código fora do JSON\n" +
            "- Os valores devem ser strings em português do Brasil";

        var (rawContent, statusCode, errorBody) =
            await CallDeepSeekAsync(systemPrompt, request.UserFeedback, ct).ConfigureAwait(false);

        if (rawContent is null)
            return (null, errorBody ?? "connection_error");

        var json = ExtractJson(rawContent);
        try
        {
            using var diffDoc = JsonDocument.Parse(json);
            if (!diffDoc.RootElement.TryGetProperty("changed_sections", out var sectionsEl))
            {
                logger.LogWarning("[ChatService.Refine] 'changed_sections' ausente (jsonSize={Size})", SanitizeForLog(json));
                logger.LogDebug("[ChatService.Refine] JSON sem changed_sections: {Raw}", json[..Math.Min(200, json.Length)]);
                return (null, json);
            }

            var sections = sectionsEl.Deserialize<Dictionary<string, string>>();
            if (sections is null || sections.Count == 0)
                return (null, json);

            logger.LogDebug("[ChatService.Refine] Diff OK — {Count} seção(ões) para user {UserId}",
                sections.Count, userId);
            return (sections, null);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[ChatService.Refine] JSON inválido (jsonSize={Size})", SanitizeForLog(json));
            logger.LogDebug("[ChatService.Refine] Conteúdo JSON inválido: {Raw}", json[..Math.Min(200, json.Length)]);
            return (null, json);
        }
    }

    public async Task<(string? RefinedContent, string? ErrorRaw)> RefineSectionAsync(
        RefineSectionRequest request, string userId, CancellationToken ct)
    {
        // Rate limit já consumido atomicamente em ChatController via TryConsumeRateLimit.
        // Spec 028 — injeta as tags de contexto (FOCO do projeto) para que o refine
        // também fique ancorado e não derive de segmento.
        var focusBlock = await BuildFocusBlockAsync(request.ProjectId, ct).ConfigureAwait(false);
        var systemPrompt =
            "Você é um especialista em validação de startups. Refine APENAS o texto da seção fornecida. " +
            "Retorne SOMENTE o texto refinado em markdown puro (sem JSON, sem aspas, sem ```). " +
            "Mantenha o tom e formato. Aplique apenas as melhorias solicitadas." + focusBlock;

        var userPrompt =
            $"Etapa: {request.StageName}\n" +
            $"Seção: {request.SectionTitle}\n\n" +
            "## Conteúdo atual da seção\n" +
            $"{request.SectionContent}\n\n" +
            "## Feedback do usuário\n" +
            $"{request.UserFeedback}\n\n" +
            "Retorne apenas o texto refinado em markdown puro, em português do Brasil.";

        var (rawContent, statusCode, errorBody) =
            await CallDeepSeekAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);

        if (rawContent is null)
            return (null, errorBody ?? "connection_error");

        var refined = CleanRefinedText(rawContent);
        if (string.IsNullOrWhiteSpace(refined))
        {
            logger.LogWarning("[ChatService.RefineSection] Conteúdo refinado vazio (rawSize={Size})",
                SanitizeForLog(rawContent));
            logger.LogDebug("[ChatService.RefineSection] Raw content: {Raw}",
                rawContent[..Math.Min(200, rawContent.Length)]);
            return (null, rawContent);
        }

        logger.LogDebug("[ChatService.RefineSection] OK — section={Section} user={UserId}",
            request.SectionKey, userId);
        return (refined, null);
    }

    /// <summary>
    /// Spec 028 — lê as tags (keywords) do projeto e monta um bloco de FOCO para
    /// ancorar o refine. Best-effort: nunca lança, retorna "" se não houver tags.
    /// </summary>
    private async Task<string> BuildFocusBlockAsync(string? projectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return string.Empty;
        try
        {
            var resp = await supabase
                .From<IdeorAI.Model.SupabaseModels.ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Limit(1)
                .Get();
            var tags = (resp.Models?.FirstOrDefault()?.Keywords ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t)).Take(10).ToList();
            if (tags.Count == 0) return string.Empty;
            return $" Mantenha o texto ancorado nestas palavras-chave do projeto (NÃO desvie delas): {string.Join(", ", tags)}.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ChatService] Falha ao buscar tags de foco (não crítico)");
            return string.Empty;
        }
    }

    private static string CleanRefinedText(string raw)
    {
        var text = raw.Trim();

        // Remove markdown code fences
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            if (text.EndsWith("```"))
                text = text[..^3];
            text = text.Trim();
        }

        // Se a LLM retornou JSON ({"refinedContent": "..."} ou {"content": "..."}), extrair
        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                foreach (var prop in new[] { "refinedContent", "refined_content", "content", "text", "value" })
                {
                    if (doc.RootElement.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        var inner = el.GetString();
                        if (!string.IsNullOrWhiteSpace(inner)) return inner.Trim();
                    }
                }
            }
            catch (JsonException) { /* não era JSON, segue */ }
        }

        // Remove aspas envolventes
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1];

        return text.Trim();
    }

    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();

        // Estratégia 1: encontrar bloco ```json ... ``` ou ``` ... ``` em qualquer posição
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = trimmed.IndexOf('\n', fenceStart);
            if (afterFence >= 0)
            {
                var fenceEnd = trimmed.LastIndexOf("```");
                if (fenceEnd > afterFence)
                {
                    var candidate = trimmed[(afterFence + 1)..fenceEnd].Trim();
                    if (candidate.StartsWith('{')) return candidate;
                }
            }
        }

        // Estratégia 2: extrair do primeiro '{' ao último '}' (robusto contra texto pré/pós JSON)
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return trimmed[firstBrace..(lastBrace + 1)];

        return trimmed;
    }
}
