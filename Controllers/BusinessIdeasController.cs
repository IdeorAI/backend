using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting("ai-generation")]
    public class BusinessIdeasController : ControllerBase
    {
        private readonly ILlmFallbackService _llmFallbackService;
        private readonly BackendMetrics _metrics;
        private readonly ILogger<BusinessIdeasController> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly IBackgroundTaskRunner _backgroundRunner;

        public BusinessIdeasController(
            ILlmFallbackService llmFallbackService,
            BackendMetrics metrics,
            ILogger<BusinessIdeasController> logger,
            IHttpClientFactory httpFactory,
            IConfiguration config,
            IBackgroundTaskRunner backgroundRunner)
        {
            _llmFallbackService = llmFallbackService;
            _metrics = metrics;
            _logger = logger;
            _httpFactory = httpFactory;
            _config = config;
            _backgroundRunner = backgroundRunner;
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

                var (ideas, suggestedName) = await GenerateSegmentIdeasAsync(req.SegmentDescription, count, ct);

                _logger.LogInformation("Successfully generated {Count} segment ideas - RequestId: {RequestId}",
                    ideas.Count, requestId);

                if (!string.IsNullOrWhiteSpace(req.ProjectId) || !string.IsNullOrWhiteSpace(req.OwnerId))
                {
                    _backgroundRunner.Run(
                        (_, ct2) => SaveSegmentIdeasToSupabaseAsync(ideas, req.ProjectId, req.OwnerId, req.Category, requestId, ct2),
                        operation: "save-segment-ideas");
                }

                return Ok(new GenerateIdeasResponse { Ideas = ideas, RequestId = requestId, SuggestedName = suggestedName });
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

                _backgroundRunner.Run(
                    (_, ct2) => SaveToSupabaseAsync(ideas, req.ProjectId, req.OwnerId, requestId, ct2),
                    operation: "save-ideas");

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

        private async Task<(List<string> Ideas, string? SuggestedName)> GenerateSegmentIdeasAsync(string segmentDescription, int count, CancellationToken ct)
        {
            string segment = (segmentDescription ?? "").Trim();
            if (segment.Length > 400) segment = segment[..400];

            var prompt = $$"""
                Você é um gerador de ideias de startups.
                Gere {{count}} ideias inovadoras para o segmento fornecido.
                Retorne APENAS um JSON válido no formato:
                {"suggested_name":"Nome Startup","ideas":[{"title":"Título curto","subtitle":"Descrição breve"}]}
                O campo "suggested_name" deve ser um nome criativo, curto (2-4 palavras) em português para uma startup neste segmento.
                Cada título deve ter no máximo 6 palavras.
                Cada subtítulo deve ter 1-2 frases curtas.
                Total por ideia: máximo 400 caracteres.
                Sem comentários, sem markdown, somente JSON.

                SEGMENTO: "{{segment}}"
                """;

            var result = await _llmFallbackService.GenerateAsync(prompt, ct: ct);
            var ideas = IdeaJsonParser.ParseIdeasJson(result.Text, count);
            var suggestedName = IdeaJsonParser.ParseSuggestedName(result.Text);
            return (ideas, suggestedName);
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
            return IdeaJsonParser.ParseSimpleIdeasJson(result.Text);
        }

        private async Task SaveToSupabaseAsync(List<string> ideas, string? projectId, string ownerId, string requestId, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var supabaseUrl = _config["Supabase:Url"];
                if (string.IsNullOrWhiteSpace(supabaseUrl))
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

                var httpRes = await supa.SendAsync(httpReq, ct);
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

        private async Task SaveSegmentIdeasToSupabaseAsync(List<string> ideas, string? projectId, string? ownerId, string? category, string requestId, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var supabaseUrl = _config["Supabase:Url"];
                if (string.IsNullOrWhiteSpace(supabaseUrl))
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

                var httpRes = await supa.SendAsync(httpReq, ct);
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
