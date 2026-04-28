using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using IdeorAI.Options;
using Microsoft.Extensions.Options;

namespace IdeorAI.Client;

public sealed class DeepSeekClient(
    IHttpClientFactory httpClientFactory,
    IOptions<DeepSeekOptions> options,
    ILogger<DeepSeekClient> logger) : ILlmClient
{
    private readonly DeepSeekOptions _opts = options.Value;

    public string ProviderName => "deepseek";
    public int Priority => 1;

    public async Task<LlmResult> GenerateAsync(string prompt, LlmOptions? options = null, CancellationToken ct = default)
    {
        var opts = options ?? new LlmOptions();
        var sw = Stopwatch.StartNew();

        var client = httpClientFactory.CreateClient("DeepSeek");

        var request = new
        {
            model = _opts.Model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = opts.Temperature,
            max_tokens = opts.MaxTokens,
            stream = false
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                "https://api.deepseek.com/v1/chat/completions", request, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[DeepSeek] Erro de rede: {ex.Message}", ex);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("[DeepSeek] Rate limit (429) atingido.");

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"[DeepSeek] Erro HTTP {(int)response.StatusCode}: {errorBody[..Math.Min(300, errorBody.Length)]}");
        }

        string responseBody;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[DeepSeek] Falha ao ler resposta: {ex.Message}", ex);
        }

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(responseBody);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[DeepSeek] Resposta não é JSON válido: {responseBody[..Math.Min(200, responseBody.Length)]}", ex);
        }

        string content;
        int inputTokens = 0, outputTokens = 0;
        try
        {
            var root = json.RootElement;
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                throw new InvalidOperationException("[DeepSeek] 'choices' vazio na resposta.");

            var contentEl = choices[0].GetProperty("message").GetProperty("content");
            var raw = LlmResponseParser.ExtractContent(contentEl);
            content = LlmResponseParser.StripCodeFences(raw);

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[DeepSeek] Estrutura de resposta inesperada: {responseBody[..Math.Min(200, responseBody.Length)]}", ex);
        }

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("[DeepSeek] Conteúdo retornado está vazio.");

        sw.Stop();
        logger.LogInformation("[DeepSeek] ✅ {Model} — {In}t in, {Out}t out, {Ms}ms",
            _opts.Model, inputTokens, outputTokens, sw.ElapsedMilliseconds);

        return new LlmResult(content, inputTokens, outputTokens, _opts.Model, ProviderName, sw.ElapsedMilliseconds);
    }
}
