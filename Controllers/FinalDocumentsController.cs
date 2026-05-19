using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

/// <summary>
/// Spec 019 — Documentos finais (Pitch Deck, Business Plan, Executive Summary).
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/documents")]
public class FinalDocumentsController : ControllerBase
{
    private readonly IDocumentSynthesisService _synth;
    private readonly IPdfExportService _pdf;
    private readonly ILogger<FinalDocumentsController> _logger;

    public FinalDocumentsController(
        IDocumentSynthesisService synth,
        IPdfExportService pdf,
        ILogger<FinalDocumentsController> logger)
    {
        _synth = synth;
        _pdf = pdf;
        _logger = logger;
    }

    [HttpPost("pitch-deck/generate")]
    public Task<IActionResult> GeneratePitchDeck(string projectId, [FromHeader(Name = "x-user-id")] string? userId, CancellationToken ct)
        => GenerateImpl(projectId, userId, ct, (p, u, c) => _synth.GeneratePitchDeckAsync(p, u, c));

    [HttpPost("business-plan/generate")]
    public Task<IActionResult> GenerateBusinessPlan(string projectId, [FromHeader(Name = "x-user-id")] string? userId, CancellationToken ct)
        => GenerateImpl(projectId, userId, ct, (p, u, c) => _synth.GenerateBusinessPlanAsync(p, u, c));

    [HttpPost("executive-summary/generate")]
    public Task<IActionResult> GenerateExecutiveSummary(string projectId, [FromHeader(Name = "x-user-id")] string? userId, CancellationToken ct)
        => GenerateImpl(projectId, userId, ct, (p, u, c) => _synth.GenerateExecutiveSummaryAsync(p, u, c));

    private async Task<IActionResult> GenerateImpl(
        string projectId,
        string? userId,
        CancellationToken ct,
        Func<string, string, CancellationToken, Task<string>> action)
    {
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            var md = await action(projectId, userId, ct);
            return Ok(new { content_md = md, cached = false });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Acesso negado" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinalDoc] erro project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Erro ao gerar documento" });
        }
    }

    [HttpGet("{type}/pdf")]
    public async Task<IActionResult> DownloadPdf(
        string projectId,
        string type,
        [FromHeader(Name = "x-user-id")] string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var allowed = new[] { "pitch-deck", "business-plan", "executive-summary" };
        if (!allowed.Contains(type)) return BadRequest(new { error = "Tipo inválido" });

        try
        {
            var bytes = await _pdf.GenerateFinalDocumentPdfAsync(projectId, type, userId, ct);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"IdeorAI-{type}.pdf\"";
            return File(bytes, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Documento ainda não gerado. Clique em Gerar primeiro." });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Acesso negado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinalDoc PDF] erro project {ProjectId} type {Type}", projectId, type);
            return StatusCode(500, new { error = "Erro ao gerar PDF" });
        }
    }
}
