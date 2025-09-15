using Microsoft.AspNetCore.Mvc;
using IdeorAI.Model;
using IdeorAI.Client;
using Microsoft.Extensions.Logging;

namespace IdeorAI.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GeminiAIController : ControllerBase
    {
        private readonly GeminiApiClient _geminiApiClient;
        private readonly ILogger<GeminiAIController> _logger;

        public GeminiAIController(GeminiApiClient geminiApiClient, ILogger<GeminiAIController> logger)
        {
            _geminiApiClient = geminiApiClient;
            _logger = logger;
        }

        [HttpPost("suggest")]
        public async Task<ActionResult<GenerateIdeasResponse>> SuggestIdeas([FromBody] GenerateIdeasRequest req)
        {
            // Obter request ID do header
            var requestId = HttpContext.Response.Headers["x-request-id"];
            
            _logger.LogInformation("SuggestIdeas request received - RequestId: {RequestId}", requestId);
            
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

            try
            {
                _logger.LogInformation("Generating ideas for segment: {Segment} - RequestId: {RequestId}", 
                    req.SegmentDescription, requestId);
                
                var ideas = await _geminiApiClient.GenerateStartupIdeasAsync(req.SeedIdea ?? "", req.SegmentDescription);
                
                _logger.LogInformation("Successfully generated {Count} ideas - RequestId: {RequestId}", 
                    ideas.Count, requestId);
                
                return Ok(new GenerateIdeasResponse { 
                    Ideas = ideas,
                    RequestId = requestId // Agora esta propriedade existe
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ideas - RequestId: {RequestId}", requestId);
                return StatusCode(500, new { 
                    error = "Falha ao gerar ideias", 
                    detail = ex.Message,
                    requestId = requestId
                });
            }
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateContent([FromBody] PromptRequest request)
        {
            var requestId = HttpContext.Response.Headers["x-request-id"];
            _logger.LogInformation("GenerateContent request received - RequestId: {RequestId}", requestId);

            try
            {
                string response = await _geminiApiClient.GenerateContentAsync(request.Prompt);
                _logger.LogInformation("Content generated successfully - RequestId: {RequestId}", requestId);
                
                return Ok(new {
                    content = response,
                    requestId = requestId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating content - RequestId: {RequestId}", requestId);
                return StatusCode(500, new {
                    error = ex.Message,
                    requestId = requestId
                });
            }
        }
        
        [HttpPost("suggest-by-segment")]
        public async Task<ActionResult<GenerateIdeasResponse>> SuggestIdeasBySegment([FromBody] SegmentIdeasRequest req)
        {
            var requestId = HttpContext.Response.Headers["x-request-id"];
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
                
                var ideas = await _geminiApiClient.GenerateSegmentIdeasAsync(req.SegmentDescription, count);
                
                _logger.LogInformation("Successfully generated {Count} segment ideas - RequestId: {RequestId}", 
                    ideas.Count, requestId);
                
                return Ok(new GenerateIdeasResponse { 
                    Ideas = ideas,
                    RequestId = requestId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating segment ideas - RequestId: {RequestId}", requestId);
                return StatusCode(500, new { 
                    error = "Falha ao gerar ideias", 
                    detail = ex.Message,
                    requestId = requestId
                });
            }
        }
    }
}