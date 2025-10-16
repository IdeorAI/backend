using Microsoft.AspNetCore.Mvc;
using IdeorAI.Model;
using IdeorAI.Client;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

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
        private readonly GeminiApiClient _geminiApiClient;
        private readonly BackendMetrics _metrics;
        private readonly ILogger<BusinessIdeasController> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;

        public BusinessIdeasController(
      GeminiApiClient geminiApiClient,
      BackendMetrics metrics,
      ILogger<BusinessIdeasController> logger,
      IHttpClientFactory httpFactory,
      IConfiguration config
  )
        {
            _geminiApiClient = geminiApiClient;
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
            {
                _logger.LogWarning("Invalid request body - RequestId: {RequestId}", requestId);
                return BadRequest("Body inválido.");
            }

            if (string.IsNullOrWhiteSpace(req.SegmentDescription))
            {
                _logger.LogWarning("SegmentDescription is required - RequestId: {RequestId}", requestId);
                return BadRequest("SegmentDescription é obrigatório.");
            }

            var count = req.Count.GetValueOrDefault(4);
            if (count < 1 || count > 6) count = 4;

            try
            {
                _logger.LogInformation("Generating {Count} ideas for segment: {Segment} - RequestId: {RequestId}",
                    count, req.SegmentDescription, requestId);

                var ideas = await _geminiApiClient.GenerateSegmentIdeasAsync(req.SegmentDescription, count, ct);

                _logger.LogInformation("Successfully generated {Count} segment ideas, returning to client - RequestId: {RequestId}",
                    ideas.Count, requestId);

                // Fire-and-forget: Salva no Supabase em background se ProjectId ou OwnerId fornecidos
                if (!string.IsNullOrWhiteSpace(req.ProjectId) || !string.IsNullOrWhiteSpace(req.OwnerId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SaveSegmentIdeasToSupabaseAsync(ideas, req.ProjectId, req.OwnerId, req.Category, requestId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Background save to Supabase failed (suggest-by-segment) - RequestId: {RequestId}", requestId);
                        }
                    });
                }

                return Ok(new GenerateIdeasResponse
                {
                    Ideas = ideas,
                    RequestId = requestId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating segment ideas - RequestId: {RequestId}", requestId);
                return StatusCode(500, new
                {
                    error = "Falha ao gerar ideias",
                    detail = ex.Message,
                    requestId = requestId
                });
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
            {
                _logger.LogWarning("Invalid body for suggest-and-save - RequestId: {RequestId}", requestId);
                return BadRequest(new { error = "OwnerId e SegmentDescription são obrigatórios.", requestId });
            }

            var count = req.Count.GetValueOrDefault(3);
            if (count < 1 || count > 6) count = 3;

            try
            {
                _logger.LogInformation("Generating {Count} ideas for segment: {Segment} - RequestId: {RequestId}",
                    count, req.SegmentDescription, requestId);

                // 1) Gera as ideias baseadas na descrição do usuário (temperatura baixa para refinar)
                var ideas = await _geminiApiClient.GenerateStartupIdeasAsync(req.SeedIdea ?? "", req.SegmentDescription, ct);

                _logger.LogInformation("Ideas generated successfully, returning to client - RequestId: {RequestId}", requestId);

                // 2) Fire-and-forget: Salva no Supabase em background sem bloquear resposta
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SaveToSupabaseAsync(ideas, req.ProjectId, req.OwnerId, requestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background save to Supabase failed - RequestId: {RequestId}", requestId);
                        // Não propaga exceção pois é background task
                    }
                });

                // 3) Retorna as ideias IMEDIATAMENTE pro frontend (otimista)
                return Ok(new
                {
                    ideas,
                    requestId,
                    saved = false // Indica que salvamento está em andamento
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Request cancelled - RequestId: {RequestId}", requestId);
                return StatusCode(499, new { error = "Cancelado pelo cliente.", requestId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in suggest-and-save - RequestId: {RequestId}", requestId);
                return StatusCode(500, new { error = "Falha ao gerar/salvar ideias.", detail = ex.Message, requestId });
            }
        }

        /// <summary>
        /// Salva ideias geradas no Supabase. Usado como background task.
        /// </summary>
        private async Task SaveToSupabaseAsync(List<string> ideas, string? projectId, string ownerId, string requestId)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var supabaseUrl = _config["Supabase:Url"];
                var serviceKey = _config["Supabase:ServiceRoleKey"];

                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
                {
                    _logger.LogError("Supabase config missing for background save - RequestId: {RequestId}", requestId);
                    return;
                }

                var supa = _httpFactory.CreateClient("supabase");

                // Atualiza o registro do projeto específico
                var patchUrl = !string.IsNullOrWhiteSpace(projectId)
                    ? $"projects?id=eq.{Uri.EscapeDataString(projectId)}"
                    : $"projects?owner_id=eq.{Uri.EscapeDataString(ownerId)}";

                var payload = new
                {
                    generated_options = ideas,
                    updated_at = DateTime.UtcNow
                };

                using var httpReq = new HttpRequestMessage(new HttpMethod("PATCH"), patchUrl)
                {
                    Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                };

                var httpRes = await supa.SendAsync(httpReq, CancellationToken.None);
                var body = await httpRes.Content.ReadAsStringAsync();

                sw.Stop();

                if (!httpRes.IsSuccessStatusCode)
                {
                    _logger.LogError("Background Supabase update failed ({Status}): {Body}, Duration: {Duration}ms - RequestId: {RequestId}",
                        (int)httpRes.StatusCode, body, sw.ElapsedMilliseconds, requestId);
                }
                else
                {
                    _logger.LogInformation("Background Supabase save successful, Duration: {Duration}ms - RequestId: {RequestId}",
                        sw.ElapsedMilliseconds, requestId);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Exception in background Supabase save, Duration: {Duration}ms - RequestId: {RequestId}",
                    sw.ElapsedMilliseconds, requestId);
            }
        }

        /// <summary>
        /// Salva ideias de segmento no Supabase incluindo categoria. Usado como background task.
        /// </summary>
        private async Task SaveSegmentIdeasToSupabaseAsync(List<string> ideas, string? projectId, string? ownerId, string? category, string requestId)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var supabaseUrl = _config["Supabase:Url"];
                var serviceKey = _config["Supabase:ServiceRoleKey"];

                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
                {
                    _logger.LogError("Supabase config missing for background save (segment) - RequestId: {RequestId}", requestId);
                    return;
                }

                var supa = _httpFactory.CreateClient("supabase");

                // Atualiza o registro do projeto específico
                if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(ownerId))
                {
                    _logger.LogWarning("Both ProjectId and OwnerId are null, cannot save to Supabase - RequestId: {RequestId}", requestId);
                    return;
                }

                var patchUrl = !string.IsNullOrWhiteSpace(projectId)
                    ? $"projects?id=eq.{Uri.EscapeDataString(projectId)}"
                    : $"projects?owner_id=eq.{Uri.EscapeDataString(ownerId!)}";

                var payload = new
                {
                    generated_options = ideas,
                    category = category,
                    updated_at = DateTime.UtcNow
                };

                using var httpReq = new HttpRequestMessage(new HttpMethod("PATCH"), patchUrl)
                {
                    Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                };

                var httpRes = await supa.SendAsync(httpReq, CancellationToken.None);
                var body = await httpRes.Content.ReadAsStringAsync();

                sw.Stop();

                if (!httpRes.IsSuccessStatusCode)
                {
                    _logger.LogError("Background Supabase update failed (segment) ({Status}): {Body}, Duration: {Duration}ms - RequestId: {RequestId}",
                        (int)httpRes.StatusCode, body, sw.ElapsedMilliseconds, requestId);
                }
                else
                {
                    _logger.LogInformation("Background Supabase save successful (segment), Duration: {Duration}ms - RequestId: {RequestId}",
                        sw.ElapsedMilliseconds, requestId);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Exception in background Supabase save (segment), Duration: {Duration}ms - RequestId: {RequestId}",
                    sw.ElapsedMilliseconds, requestId);
            }
        }


    }
}