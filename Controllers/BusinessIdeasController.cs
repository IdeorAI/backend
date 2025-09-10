using Microsoft.AspNetCore.Mvc;
using IdeorAI.Model;
using IdeorAI.Client;


namespace IdeorAI.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
   public class GeminiAIController : ControllerBase
    {
        private readonly GeminiApiClient _geminiApiClient;
        public GeminiAIController(GeminiApiClient geminiApiClient)
        {
            _geminiApiClient = geminiApiClient;
        }

        [HttpPost("suggest")]
        public async Task<ActionResult<GenerateIdeasResponse>> SuggestIdeas([FromBody] GenerateIdeasRequest req)
        {
            if (req == null) return BadRequest("Body inválido.");
            if (string.IsNullOrWhiteSpace(req.SegmentDescription))
                return BadRequest("SegmentDescription é obrigatório.");

            try
            {
                var ideas = await _geminiApiClient.GenerateStartupIdeasAsync(req.SeedIdea ?? "", req.SegmentDescription);
                return Ok(new GenerateIdeasResponse { Ideas = ideas });
            }
            catch (Exception ex)
            {
                // logue ex.Message + response body do Gemini se quiser diagnosticar
                return StatusCode(500, new { error = "Falha ao gerar ideias", detail = ex.Message });
            }
        }


        [HttpPost("generate")]
        public async Task<IActionResult> GenerateContent([FromBody] PromptRequest request)
        {
            try
            {
                string response = await _geminiApiClient.GenerateContentAsync(request.Prompt);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}