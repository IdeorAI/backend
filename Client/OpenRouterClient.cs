using System.Net.Http.Json;
using System.Text.Json;

namespace IdeorAI.Client;

/// <summary>
/// Cliente para API do OpenRouter (compatível com OpenAI)
/// </summary>
public class OpenRouterClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _model;
    private readonly ILogger<OpenRouterClient> _logger;

    public OpenRouterClient(
        IHttpClientFactory httpClientFactory,
        string model,
        ILogger<OpenRouterClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _model = model;
        _logger = logger;
    }

    public async Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default)
    {
        _logger.LogInformation("[OpenRouter] Calling model {Model}", _model);

        var client = _httpClientFactory.CreateClient("OpenRouter");

        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 8000
        };

        var response = await client.PostAsJsonAsync(
            "https://openrouter.ai/api/v1/chat/completions",
            request,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[OpenRouter] API error {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new HttpRequestException($"OpenRouter API returned {response.StatusCode}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (json == null)
            throw new InvalidOperationException("OpenRouter returned empty response");

        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException("OpenRouter returned empty content");

        _logger.LogInformation("[OpenRouter] Success. Content length: {Length}", content.Length);
        return content;
    }
}
