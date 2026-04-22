using IdeorAI.Model.DTOs;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

[ApiController]
[Route("api/projects/{projectId}/go-or-pivot")]
public class GoPivotController : ControllerBase
{
    private readonly IGoPivotService _goPivotService;
    private readonly ILogger<GoPivotController> _logger;

    public GoPivotController(IGoPivotService goPivotService, ILogger<GoPivotController> logger)
    {
        _goPivotService = goPivotService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GoPivotResponseDto>> Get(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var result = await _goPivotService.GetExistingAsync(projectId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<GoPivotResponseDto>> Evaluate(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("[GoPivot] Evaluating project {ProjectId}", projectId);
        try
        {
            var result = await _goPivotService.EvaluateAsync(projectId, userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("insuficientes"))
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoPivot] Falha ao avaliar projeto {ProjectId}", projectId);
            return StatusCode(502, new { error = "Falha ao obter avaliação do modelo de IA. Tente novamente." });
        }
    }

    [HttpPost("override")]
    public async Task<IActionResult> ConfirmOverride(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId,
        [FromBody] GoPivotOverrideDto dto)
    {
        if (!dto.Confirm)
            return BadRequest(new { error = "Confirme explicitamente o override (confirm: true)." });

        try
        {
            await _goPivotService.ConfirmOverrideAsync(projectId, userId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
