using IdeorAI.Model.DTOs;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IdeorAI.Controllers;

/// <summary>
/// Endpoints do "Resumo Financeiro" (Spec 022 v2). Gera/lê a DRE como artefato
/// próprio derivado da Etapa 4. Segue o padrão do GoPivotController.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/financial-summary")]
public class FinancialSummaryController : ControllerBase
{
    private readonly IFinancialSummaryService _service;
    private readonly IProjectService _projectService;
    private readonly IPdfExportService _pdfExportService;
    private readonly IFinancialVariableService _financialVariableService;
    private readonly ILogger<FinancialSummaryController> _logger;

    public FinancialSummaryController(
        IFinancialSummaryService service,
        IProjectService projectService,
        IPdfExportService pdfExportService,
        IFinancialVariableService financialVariableService,
        ILogger<FinancialSummaryController> logger)
    {
        _service = service;
        _projectService = projectService;
        _pdfExportService = pdfExportService;
        _financialVariableService = financialVariableService;
        _logger = logger;
    }

    private async Task<bool> CanAccessAsync(Guid projectId, Guid userId)
    {
        var project = await _projectService.GetByIdAsync(projectId, userId);
        return project != null;
    }

    [HttpGet]
    public async Task<ActionResult<FinancialSummaryDto>> Get(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await CanAccessAsync(projectId, userId)) return NotFound();

        var result = await _service.GetExistingAsync(projectId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    [EnableRateLimiting("ai-generation")]
    public async Task<ActionResult<FinancialSummaryDto>> Generate(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await CanAccessAsync(projectId, userId)) return NotFound();

        try
        {
            var result = await _service.GenerateAsync(projectId, userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Etapa 4 não concluída ou sem DRE — erro de pré-condição do usuário.
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinSummary] Falha ao gerar Resumo Financeiro para project {ProjectId}", projectId);
            return StatusCode(502, new { error = "Falha ao gerar o Resumo Financeiro. Tente novamente." });
        }
    }

    /// <summary>
    /// Spec 027 (C1/C2/C4) — write-back das variáveis financeiras ao editar a DRE.
    /// Chamado pelo /financeiro APÓS salvar a DRE: recalcula as âncoras das 12 colunas,
    /// trava (locked), reescreve a etapa 4 e a marca desatualizada. Tolerante a falha.
    /// </summary>
    [HttpPost("sync-variables")]
    public async Task<IActionResult> SyncVariables(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId,
        [FromBody] SyncVariablesRequest body)
    {
        if (!await CanAccessAsync(projectId, userId)) return NotFound();
        if (string.IsNullOrWhiteSpace(body?.Dre))
            return UnprocessableEntity(new { error = "DRE ausente no corpo da requisição." });

        try
        {
            await _financialVariableService.SyncFromDreAsync(projectId, body.Dre);
            return NoContent();
        }
        catch (Exception ex)
        {
            // Write-back é best-effort; não deve quebrar o save da DRE no cliente.
            _logger.LogWarning(ex, "[FinSummary] Falha no sync de variáveis financeiras para project {ProjectId}", projectId);
            return NoContent();
        }
    }

    public sealed class SyncVariablesRequest
    {
        /// <summary>JSON da DRE editada (objeto com "linhas"[], ou wrapper { dre }).</summary>
        public string? Dre { get; set; }
    }

    /// <summary>
    /// Spec 024 — preenche a DRE por IA (modo manual). Uso único: recusa se já preenchida.
    /// </summary>
    [HttpPost("ai-fill")]
    [EnableRateLimiting("ai-generation")]
    public async Task<ActionResult<FinancialSummaryDto>> AiFill(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await CanAccessAsync(projectId, userId)) return NotFound();

        try
        {
            var result = await _service.AiFillAsync(projectId, userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinSummary] Falha no ai-fill da DRE para project {ProjectId}", projectId);
            return StatusCode(502, new { error = "Falha ao preencher a DRE com IA. Tente novamente." });
        }
    }

    /// <summary>
    /// Spec 022 — baixa o Resumo Financeiro (tabela DRE atualizada) em PDF.
    /// </summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> DownloadPdf(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId,
        CancellationToken ct)
    {
        try
        {
            var bytes = await _pdfExportService.GenerateFinancialSummaryPdfAsync(
                projectId.ToString(), userId.ToString(), ct);
            Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"IdeorAI-resumo-financeiro-{projectId}.pdf\"";
            return File(bytes, "application/pdf");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(new { error = "Resumo Financeiro não encontrado." }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinSummaryPdf] Falha ao gerar PDF para project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Erro ao gerar o PDF do Resumo Financeiro." });
        }
    }
}
