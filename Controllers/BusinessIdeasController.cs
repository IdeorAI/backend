using Microsoft.AspNetCore.Mvc;
using IdeorAI.Model;
using IdeorAI.Client;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using IdeorAI.Services;

namespace IdeorAI.Api.Controllers
{
    public class SuggestAndSaveRequest
    {
        public string OwnerId { get; set; } = default!;
        public string? ProjectId { get; set; }
        public string SegmentDescription { get; set; } = default!;
        public int? Count { get; set; }
        public string? SeedIdea { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class BusinessIdeasController : ControllerBase
    {
        private readonly ILlmFallbackService _llmFallbackService;
        private readonly BackendMetrics _metrics;
        private readonly ILogger<BusinessIdeasController> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;

        public BusinessIdeasController(
            ILlmFallbackService llmFallbackService,
            BackendMetrics metrics,
            ILogger<BusinessIdeasController> logger,
            IHttpClientFactory httpFactory,
            IConfiguration config)
        {
            _llmFallbackService = llmFallbackService;
            _metrics = metrics;
            _logger = logger;
            _httpFactory = httpFactory;
            _config = config;
        }

        [HttpPost("suggest-by-segment")]
        public async Task<ActionResult<GenerateIdeasResponse>> SuggestIdeasBySegment([FromBody] SegmentIdeasRequest req, CancellationToken ct)
        {
            var requestId =
               HttpContext.Request.Headers["x-request-id"].ToString()
               ?? Activity.Current?.Id
               ?? Guid.NewGuid().ToString();

            _logger.LogInformation("SuggestIdeasBySegment request received - RequestId: {RequestId}", requestId);

            if (req == null)
                return BadRequest("Body inválido.");

            if (string.IsNullOrWhiteSpace(req.SegmentDescription))
                return BadRequest("SegmentDescription é obrigatório.");

            var count = req.Count.GetValueOrDefault(4);
            if (count < 1 || count > 6) count = 4;

            try
            {
                _logger.LogInformation("Generating {Count} ideas for segment: {Segment} - RequestId: {RequestId}",
                    count, req.SegmentDescription, requestId);

                var ideas = await GenerateSegmentIdeasAsync(req.SegmentDescription, count, ct);

                _logger.LogInformation("Successfully generated {Count} segment ideas - RequestId: {RequestId}",
                    ideas.Count, requestId);

                if (!string.IsNullOrWhiteSpace(req.ProjectId) || !string.IsNullOrWhiteSpace(req.OwnerId))
                {
                    _ = Task.Run(async () =>
                    {
                        try { await SaveSegmentIdeasToSupabaseAsync(ideas, req.ProjectId, req.OwnerId, req.Category, requestId); }
                        catch (Exception ex) { _logger.LogError(ex, "Background save failed (suggest-by-segment) - RequestId: {RequestId}", requestId); }
                    });
                }

                return Ok(new GenerateIdeasResponse { Ideas = ideas, RequestId = requestId });
            }
            catch (Exception ex) when (ex is IdeorAI.Client.LlmUnavailableException || (ex is InvalidOperationException && ex.Message.Contains("falharam")))
            {
                _logger.LogWarning(ex, "Rate limit OpenRouter (suggest-by-segment) - RequestId: {RequestId}", requestId);
                return StatusCode(429, new { error = "Limite de requisições da IA atingido. Aguarde 1 minuto e tente novamente.", requestId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating segment ideas - RequestId: {RequestId}", requestId);
                return StatusCode(500, new { error = "Falha ao gerar ideias", requestId });
            }
        }

        [HttpPost("suggest-and-save")]
        public async Task<IActionResult> SuggestAndSave([FromBody] SuggestAndSaveRequest req, CancellationToken ct)
        {
            var requestId =
                HttpContext.Request.Headers["x-request-id"].ToString()
                ?? Activity.Current?.Id
                ?? Guid.NewGuid().ToString();

            if (req == null || string.IsNullOrWhiteSpace(req.OwnerId) || string.IsNullOrWhiteSpace(req.SegmentDescription))
                return BadRequest(new { error = "OwnerId e SegmentDescription são obrigatórios.", requestId });

            var count = req.Count.GetValueOrDefault(3);
            if (count < 1 || count > 6) count = 3;

            try
            {
                var ideas = await GenerateStartupIdeasAsync(req.SeedIdea ?? "", req.SegmentDescription, ct);

                _ = Task.Run(async () =>
                {
                    try { await SaveToSupabaseAsync(ideas, req.ProjectId, req.OwnerId, requestId); }
                    catch (Exception ex) { _logger.LogError(ex, "Background save failed - RequestId: {RequestId}", requestId); }
                });

                return Ok(new { ideas, requestId, saved = false });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, new { error = "Cancelado pelo cliente.", requestId });
            }
            catch (Exception ex) when (ex is IdeorAI.Client.LlmUnavailableException || (ex is InvalidOperationException && ex.Message.Contains("falharam")))
            {
                _logger.LogWarning(ex, "Rate limit OpenRouter (suggest-and-save) - RequestId: {RequestId}", requestId);
                return StatusCode(429, new { error = "Limite de requisições da IA atingido. Aguarde 1 minuto e tente novamente.", requestId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in suggest-and-save - RequestId: {RequestId}", requestId);
                return StatusCode(500, new { error = "Falha ao gerar/salvar ideias.", requestId });
            }
        }

        private async Task<List<string>> GenerateSegmentIdeasAsync(string segmentDescription, int count, CancellationToken ct)
        {
            string segment = (segmentDescription ?? "").Trim();
            if (segment.Length > 400) segment = segment[..400];

            var prompt = $$"""
                Você é um gerador de ideias de startups.
                Gere {{count}} ideias inovadoras para o segmento fornecido.
                Retorne APENAS um JSON válido no formato:
                {"ideas":[{"title":"Título curto","subtitle":"Descrição breve"}]}
                Cada título deve ter no máximo 6 palavras.
                Cada subtítulo deve ter 1-2 frases curtas.
                Total por ideia: máximo 400 caracteres.
                Sem comentários, sem markdown, somente JSON.

                SEGMENTO: "{{segment}}"
                """;

            var result = await _llmFallbackService.GenerateAsync(prompt, ct: ct);
            return ParseIdeasJson(result.Text, count);
        }

        private async Task<List<string>> GenerateStartupIdeasAsync(string seedIdea, string segmentDescription, CancellationToken ct)
        {
            string seed = (seedIdea ?? "").Trim();
            if (seed.Length > 400) seed = seed[..400];
            string segment = (segmentDescription ?? "").Trim();
            if (segment.Length > 400) segment = segment[..400];

            var prompt = $$"""
                Você é um gerador de ideias de startups.
                Seu trabalho é propor 3 versões da ideia, cada uma com no máximo 400 caracteres.
                Baseie-se na ideia semente e na descrição do segmento informadas.
                Retorne APENAS JSON com o formato:
                {"ideas":["...","...","..."]}
                Sem comentários, sem markdown.

                IDEIA_SEMENTE: "{{seed}}"
                SEGMENTO: "{{segment}}"
                """;

            var result = await _llmFallbackService.GenerateAsync(prompt, ct: ct);
            return ParseSimpleIdeasJson(result.Text);
        }

        private static List<string> ParseIdeasJson(string raw, int expectedCount)
        {
            var cleaned = JsonSanitizer.ExtractJson(raw);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var ideas = new List<string>();

            if (root.TryGetProperty("ideas", out var ideasEl))
            {
                foreach (var item in ideasEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        ideas.Add(item.GetString() ?? "");
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        var subtitle = item.TryGetProperty("subtitle", out var s) ? s.GetString() ?? "" : "";
                        ideas.Add(string.IsNullOrEmpty(subtitle) ? title : $"{title} — {subtitle}");
                    }
                }
            }

            return ideas;
        }

        private static List<string> ParseSimpleIdeasJson(string raw)
        {
            var cleaned = JsonSanitizer.ExtractJson(raw);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var ideas = new List<string>();

            if (root.TryGetProperty("ideas", out var ideasEl))
                foreach (var item in ideasEl.EnumerateArray())
                    ideas.Add(item.GetString() ?? "");

            return ideas;
        }

        private async Task SaveToSupabaseAsync(List<string> ideas, string? projectId, string ownerId, string requestId)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var supabaseUrl = _config["Supabase:Url"];
                var serviceKey = _config["Supabase:ServiceRoleKey"];

                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
                {
                    _logger.LogError("Supabase config missing - RequestId: {RequestId}", requestId);
                    return;
                }

                var supa = _httpFactory.CreateClient("supabase");
                var patchUrl = !string.IsNullOrWhiteSpace(projectId)
                    ? $"projects?id=eq.{Uri.EscapeDataString(projectId)}"
                    : $"projects?owner_id=eq.{Uri.EscapeDataString(ownerId)}";

                var payload = new { generated_options = ideas, updated_at = DateTime.UtcNow };
                using var httpReq = new HttpRequestMessage(new HttpMethod("PATCH"), patchUrl)
                {
                    Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                };

                var httpRes = await supa.SendAsync(httpReq, CancellationToken.None);
                sw.Stop();

                if (!httpRes.IsSuccessStatusCode)
                    _logger.LogError("Supabase update failed ({Status}) - RequestId: {RequestId}", (int)httpRes.StatusCode, requestId);
                else
                    _logger.LogInformation("Supabase save OK {Duration}ms - RequestId: {RequestId}", sw.ElapsedMilliseconds, requestId);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Exception in background Supabase save - RequestId: {RequestId}", requestId);
            }
        }

        private async Task SaveSegmentIdeasToSupabaseAsync(List<string> ideas, string? projectId, string? ownerId, string? category, string requestId)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var supabaseUrl = _config["Supabase:Url"];
                var serviceKey = _config["Supabase:ServiceRoleKey"];

                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
                {
                    _logger.LogError("Supabase config missing (segment) - RequestId: {RequestId}", requestId);
                    return;
                }

                if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(ownerId))
                {
                    _logger.LogWarning("Both ProjectId and OwnerId are null - RequestId: {RequestId}", requestId);
                    return;
                }

                var supa = _httpFactory.CreateClient("supabase");
                var patchUrl = !string.IsNullOrWhiteSpace(projectId)
                    ? $"projects?id=eq.{Uri.EscapeDataString(projectId)}"
                    : $"projects?owner_id=eq.{Uri.EscapeDataString(ownerId!)}";

                var payload = new { generated_options = ideas, category, updated_at = DateTime.UtcNow };
                using var httpReq = new HttpRequestMessage(new HttpMethod("PATCH"), patchUrl)
                {
                    Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                };

                var httpRes = await supa.SendAsync(httpReq, CancellationToken.None);
                sw.Stop();

                if (!httpRes.IsSuccessStatusCode)
                    _logger.LogError("Supabase update failed (segment) ({Status}) - RequestId: {RequestId}", (int)httpRes.StatusCode, requestId);
                else
                    _logger.LogInformation("Supabase save OK (segment) {Duration}ms - RequestId: {RequestId}", sw.ElapsedMilliseconds, requestId);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Exception in background Supabase save (segment) - RequestId: {RequestId}", requestId);
            }
        }
    }
}
