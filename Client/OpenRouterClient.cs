using System.Net.Http.Json;
using System.Text.Json;

namespace IdeorAI.Client;

/// <summary>
/// Cliente para API do OpenRouter (compatível com OpenAI).
/// Suporta rodízio entre múltiplos modelos para evitar rate limiting.
/// </summary>
public class OpenRouterClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<string> _models;
    private readonly ILogger<OpenRouterClient> _logger;

    public OpenRouterClient(
        IHttpClientFactory httpClientFactory,
        IEnumerable<string> models,
        ILogger<OpenRouterClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _models = models.ToList().AsReadOnly();
        _logger = logger;

        if (_models.Count == 0)
            throw new ArgumentException("Pelo menos um modelo OpenRouter deve ser configurado.");
    }

    public async Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default)
    {
        var result = await GenerateContentWithMetadataAsync(prompt, ct);
        return result.Text;
    }

    /// <summary>
    /// Retorna texto + tokens reais.
    /// Estratégia de rodízio: tenta cada modelo em sequência em caso de 429.
    /// Após esgotar todos os modelos, aguarda 15s e repete (até 2 rodadas completas).
    /// </summary>
    public async Task<GeminiResult> GenerateContentWithMetadataAsync(string prompt, CancellationToken ct = default)
    {
        const int maxRounds = 2;

        for (int round = 0; round < maxRounds; round++)
        {
            for (int i = 0; i < _models.Count; i++)
            {
                var model = _models[i];
                _logger.LogInformation("[OpenRouter] Tentando modelo {Model} (rodada {Round}, slot {Slot})",
                    model, round + 1, i + 1);

                var client = _httpClientFactory.CreateClient("OpenRouter");

                var request = new
                {
                    model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.7,
                    max_tokens = 8000
                };

                HttpResponseMessage response;
                try
                {
                    response = await client.PostAsJsonAsync(
                        "https://openrouter.ai/api/v1/chat/completions", request, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[OpenRouter] Erro de rede no modelo {Model}, tentando próximo", model);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("[OpenRouter] Rate limit (429) no modelo {Model}, tentando próximo", model);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("[OpenRouter] Erro {StatusCode} no modelo {Model}: {Body}",
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
                    _logger.LogWarning(ex, "[OpenRouter] Falha ao ler body do modelo {Model}", model);
                    continue;
                }

                JsonDocument json;
                try
                {
                    json = JsonDocument.Parse(responseBody);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[OpenRouter] Body não é JSON válido no modelo {Model}: {Preview}",
                        model, responseBody[..Math.Min(200, responseBody.Length)]);
                    continue;
                }

                var root = json.RootElement;

                string? content;
                try
                {
                    var choices = root.GetProperty("choices");
                    if (choices.GetArrayLength() == 0)
                    {
                        _logger.LogWarning("[OpenRouter] Modelo {Model} retornou choices vazio", model);
                        continue;
                    }

                    var contentEl = choices[0].GetProperty("message").GetProperty("content");

                    if (contentEl.ValueKind == JsonValueKind.Array)
                    {
                        // Minimax retorna [{type:"text", text:"..."}]
                        var sb = new System.Text.StringBuilder();
                        foreach (var part in contentEl.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textPart))
                                sb.Append(textPart.GetString());
                        }
                        content = sb.ToString();
                    }
                    else
                    {
                        content = contentEl.GetString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[OpenRouter] Estrutura de resposta inesperada no modelo {Model}: {Preview}",
                        model, responseBody[..Math.Min(200, responseBody.Length)]);
                    continue;
                }

                if (!string.IsNullOrEmpty(content))
                    content = StripCodeFences(content);

                if (string.IsNullOrEmpty(content))
                {
                    _logger.LogWarning("[OpenRouter] Modelo {Model} retornou conteúdo vazio", model);
                    continue;
                }

                var inputTokens  = 0;
                var outputTokens = 0;
                if (root.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens",     out var pt))  inputTokens  = pt.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
                }

                _logger.LogInformation("[OpenRouter] ✅ Sucesso com modelo {Model} — input: {In}t, output: {Out}t",
                    model, inputTokens, outputTokens);
                return new GeminiResult(content, inputTokens, outputTokens, model);
            }

            // Todos os modelos desta rodada retornaram 429/erro — aguardar antes da próxima rodada
            if (round < maxRounds - 1)
            {
                _logger.LogWarning("[OpenRouter] Todos os {Count} modelo(s) falharam (rodada {Round}). Aguardando 20s antes de tentar novamente...",
                    _models.Count, round + 1);
                await Task.Delay(20_000, ct);
            }
        }

        throw new InvalidOperationException(
            $"OpenRouter: todos os modelos ({string.Join(", ", _models)}) falharam após {maxRounds} rodadas. " +
            "Verifique os limites de requisição das suas chaves ou aguarde alguns minutos.");
    }

    private static string StripCodeFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            var firstNewline = t.IndexOf('\n');
            if (firstNewline >= 0) t = t[(firstNewline + 1)..];
            if (t.EndsWith("```")) t = t[..^3].TrimEnd();
        }
        return t;
    }
}
