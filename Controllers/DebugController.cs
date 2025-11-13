using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using Serilog;
using IdeorAI.Client;

namespace IdeorAI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DebugController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiApiClient _geminiClient;

    public DebugController(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        GeminiApiClient geminiClient)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _geminiClient = geminiClient;
    }

    /// <summary>
    /// Testa conexão com API do Gemini sem gerar conteúdo
    /// GET /api/debug/test-gemini
    /// </summary>
    [HttpGet("test-gemini")]
    public async Task<IActionResult> TestGeminiConnection()
    {
        try
        {
            var apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("Gemini__ApiKey");
            var model = _configuration["Gemini:Model"] ?? "gemini-2.5-flash";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Ok(new
                {
                    success = false,
                    error = "API Key do Gemini não configurada",
                    message = "Configure a variável de ambiente 'Gemini__ApiKey' no Render.com",
                    apiKeyPresent = false,
                    apiKeyLength = 0
                });
            }

            // Mascarar API Key para segurança (mostrar só início e fim)
            var maskedKey = apiKey.Length > 10
                ? $"{apiKey.Substring(0, 5)}...{apiKey.Substring(apiKey.Length - 5)}"
                : "***";

            // Tentar fazer uma requisição simples à API
            var client = _httpClientFactory.CreateClient("GeminiApiClient");
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}?key={apiKey}";

            Log.Information("Testando conexão com Gemini API: {Url}", url.Replace(apiKey, "***"));

            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return Ok(new
                {
                    success = true,
                    message = "✅ Conexão com Gemini API bem-sucedida!",
                    apiKeyPresent = true,
                    apiKeyMasked = maskedKey,
                    apiKeyLength = apiKey.Length,
                    model = model,
                    statusCode = (int)response.StatusCode,
                    responsePreview = content.Length > 200 ? content.Substring(0, 200) + "..." : content
                });
            }
            else
            {
                return Ok(new
                {
                    success = false,
                    error = $"Gemini API retornou erro: {response.StatusCode}",
                    apiKeyPresent = true,
                    apiKeyMasked = maskedKey,
                    apiKeyLength = apiKey.Length,
                    model = model,
                    statusCode = (int)response.StatusCode,
                    statusCodeName = response.StatusCode.ToString(),
                    responseBody = content,
                    possibleCauses = GetPossibleCauses(response.StatusCode)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao testar conexão com Gemini API");
            return Ok(new
            {
                success = false,
                error = "Exceção ao conectar com Gemini API",
                exceptionType = ex.GetType().Name,
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Retorna informações de configuração (sem expor credenciais)
    /// GET /api/debug/config
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfiguration()
    {
        var apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("Gemini__ApiKey");
        var connectionString = _configuration.GetConnectionString("SupabaseConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SupabaseConnection");

        return Ok(new
        {
            gemini = new
            {
                apiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey),
                apiKeyLength = apiKey?.Length ?? 0,
                apiKeySource = !string.IsNullOrWhiteSpace(_configuration["Gemini:ApiKey"]) ? "appsettings.json" : "Environment Variable",
                model = _configuration["Gemini:Model"],
                temperature = _configuration["Gemini:Temperature"],
                maxTokens = _configuration["Gemini:MaxOutputTokens"]
            },
            database = new
            {
                connectionStringConfigured = !string.IsNullOrWhiteSpace(connectionString),
                connectionStringLength = connectionString?.Length ?? 0,
                connectionStringSource = !string.IsNullOrWhiteSpace(_configuration.GetConnectionString("SupabaseConnection"))
                    ? "appsettings.json" : "Environment Variable",
                hasIPv4First = connectionString?.Contains("IP Address Preference=IPv4First") ?? false,
                host = ExtractHost(connectionString)
            },
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
            serverTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
        });
    }

    private static List<string> GetPossibleCauses(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => new List<string>
            {
                "API Key inválida ou expirada",
                "API Key não tem permissões necessárias",
                "Verifique se a API Key está correta no Render.com"
            },
            System.Net.HttpStatusCode.Forbidden => new List<string>
            {
                "API Key bloqueada ou suspensa",
                "Quota de uso excedida",
                "Região geográfica bloqueada",
                "Billing não configurado no Google Cloud"
            },
            System.Net.HttpStatusCode.ServiceUnavailable => new List<string>
            {
                "API do Gemini temporariamente indisponível",
                "Rate limiting ou throttling",
                "Manutenção programada do Google",
                "Problema de rede entre Render e Google Cloud"
            },
            System.Net.HttpStatusCode.TooManyRequests => new List<string>
            {
                "Limite de requisições por minuto excedido",
                "Aguarde alguns segundos e tente novamente",
                "Considere upgrade do plano Gemini API"
            },
            _ => new List<string>
            {
                $"Erro HTTP {(int)statusCode}: {statusCode}",
                "Verifique logs do Render.com para mais detalhes",
                "Teste manualmente: https://aistudio.google.com/apikey"
            }
        };
    }

    /// <summary>
    /// Testa geração de documento com prompt simples (isolado do DocumentGenerationService)
    /// POST /api/debug/test-document-generation
    /// </summary>
    [HttpPost("test-document-generation")]
    public async Task<IActionResult> TestDocumentGeneration()
    {
        try
        {
            Log.Information("[DEBUG] Iniciando teste de geração de documento");

            var apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("Gemini__ApiKey");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Ok(new
                {
                    success = false,
                    error = "API Key do Gemini não configurada",
                    stage = "config-check"
                });
            }

            Log.Information("[DEBUG] API Key presente. Comprimento: {Length}", apiKey.Length);

            // Prompt simples para teste (similar ao usado em /idea/descreva)
            var testPrompt = @"Gere 3 ideias inovadoras de startup para o segmento de saúde no Brasil.

Retorne APENAS um JSON no formato:
{
  ""ideas"": [
    ""Ideia 1"",
    ""Ideia 2"",
    ""Ideia 3""
  ]
}";

            Log.Information("[DEBUG] Prompt de teste criado. Comprimento: {Length} chars", testPrompt.Length);
            Log.Information("[DEBUG] Chamando Gemini API com modelo fixo: gemini-2.5-flash");

            var startTime = DateTime.UtcNow;

            // Chamar diretamente o GeminiApiClient com modelo fixo (não usa rotação)
            var generatedContent = await _geminiClient.GenerateContentAsync(testPrompt, stage: "debug");

            var elapsed = DateTime.UtcNow - startTime;

            Log.Information("[DEBUG] Gemini API respondeu com sucesso. Tempo: {Elapsed}ms", elapsed.TotalMilliseconds);
            Log.Information("[DEBUG] Conteúdo gerado. Comprimento: {Length} chars", generatedContent.Length);

            return Ok(new
            {
                success = true,
                message = "✅ Geração de documento funcionando!",
                promptLength = testPrompt.Length,
                responseLength = generatedContent.Length,
                elapsedMs = elapsed.TotalMilliseconds,
                model = "gemini-flash-1.5", // Modelo usado para debug (configurado em GeminiApiClient.GetModelForStage)
                generatedContent = generatedContent.Length > 500 ? generatedContent.Substring(0, 500) + "..." : generatedContent
            });
        }
        catch (HttpRequestException httpEx)
        {
            Log.Error(httpEx, "[DEBUG] Erro HTTP ao chamar Gemini API");
            return Ok(new
            {
                success = false,
                error = "Erro HTTP ao chamar Gemini API",
                exceptionType = httpEx.GetType().Name,
                message = httpEx.Message,
                statusCode = httpEx.StatusCode?.ToString() ?? "Unknown",
                stage = "gemini-api-call"
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DEBUG] Erro geral ao testar geração de documento");
            return Ok(new
            {
                success = false,
                error = "Erro ao testar geração de documento",
                exceptionType = ex.GetType().Name,
                message = ex.Message,
                stackTrace = ex.StackTrace,
                stage = "general-error"
            });
        }
    }

    private static string ExtractHost(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "N/A";

        var hostMatch = System.Text.RegularExpressions.Regex.Match(connectionString, @"Host=([^;]+)");
        return hostMatch.Success ? hostMatch.Groups[1].Value : "N/A";
    }
}
