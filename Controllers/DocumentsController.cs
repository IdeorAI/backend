using IdeorAI.Model.DTOs;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IdeorAI.Controllers;

/// <summary>
/// Controller para geração de documentos via IA
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentGenerationService _documentService;
    private readonly IPdfExportService _pdfExportService;
    private readonly IStageService _stageService;
    private readonly IStageSummaryService _stageSummaryService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentGenerationService documentService,
        IPdfExportService pdfExportService,
        IStageService stageService,
        IStageSummaryService stageSummaryService,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _pdfExportService = pdfExportService;
        _stageService = stageService;
        _stageSummaryService = stageSummaryService;
        _logger = logger;
    }

    /// <summary>
    /// Gera um documento para uma etapa específica
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GenerateDocumentResponseDto>> GenerateDocument(
        Guid projectId,
        [FromBody] GenerateDocumentDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Generating document for project {ProjectId}, stage {Stage}",
            projectId, dto.Phase);

        var task = await _documentService.GenerateDocumentAsync(
            projectId,
            userId,
            dto.Phase,
            dto.Inputs);

        if (task == null)
        {
            return BadRequest(new { error = "Failed to generate document. Check project access and stage." });
        }

        // Validar se o JSON foi salvo corretamente
        bool stageSaved = false;
        try
        {
            var extractedJson = JsonSanitizer.ExtractJson(task.Content ?? "");
            if (JsonSanitizer.TryValidateSchema(extractedJson, dto.Phase, out _, out _))
            {
                stageSaved = true;
            }
        }
        catch
        {
            stageSaved = false;
        }

        return Ok(new GenerateDocumentResponseDto
        {
            TaskId = task.Id,
            Phase = task.Phase,
            GeneratedContent = task.Content ?? "",
            ModelUsed = "rotação-inteligente",
            TokensUsed = (task.Content ?? "").Length / 4,
            Status = task.Status,
            StageSaved = stageSaved
        });
    }

    /// <summary>
    /// Regenera um documento existente com novos inputs
    /// </summary>
    [HttpPost("~/api/documents/{taskId}/regenerate")]
    public async Task<ActionResult<GenerateDocumentResponseDto>> RegenerateDocument(
        Guid taskId,
        [FromBody] Dictionary<string, string> newInputs,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Regenerating document for task {TaskId}", taskId);

        // Buscar a task atual para obter projectId e stage
        // Precisamos invalidar etapas posteriores antes de regenerar
        var existingTask = await _stageService.GetTaskByIdAsync(taskId, userId);
        if (existingTask == null)
        {
            return NotFound(new { error = "Task not found or access denied" });
        }

        // Invalidar etapas posteriores ao regenerar
        try
        {
            _logger.LogInformation("Invalidando etapas posteriores a {Stage} no projeto {ProjectId}", 
                existingTask.Phase, existingTask.ProjectId);
            await _stageSummaryService.DeleteSubsequentStagesAsync(existingTask.ProjectId, existingTask.Phase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao invalidar etapas posteriores (continuando)");
        }

        var task = await _documentService.RegenerateDocumentAsync(taskId, userId, newInputs);

        if (task == null)
        {
            return NotFound(new { error = "Task not found or access denied" });
        }

        // Validar se o JSON foi salvo corretamente
        bool stageSaved = false;
        try
        {
            var extractedJson = JsonSanitizer.ExtractJson(task.Content ?? "");
            if (JsonSanitizer.TryValidateSchema(extractedJson, task.Phase, out _, out _))
            {
                stageSaved = true;
            }
        }
        catch
        {
            stageSaved = false;
        }

        return Ok(new GenerateDocumentResponseDto
        {
            TaskId = task.Id,
            Phase = task.Phase,
            GeneratedContent = task.Content ?? "",
            ModelUsed = "rotação-inteligente",
            TokensUsed = (task.Content ?? "").Length / 4,
            Status = task.Status,
            StageSaved = stageSaved
        });
    }

    /// <summary>
    /// Refina um documento existente com feedback do usuário
    /// </summary>
    [HttpPost("~/api/documents/{taskId}/refine")]
    public async Task<ActionResult<GenerateDocumentResponseDto>> RefineDocument(
        Guid taskId,
        [FromBody] RefineDocumentDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Refining document for task {TaskId}", taskId);

        var task = await _documentService.RefineDocumentAsync(taskId, userId, dto.Feedback);

        if (task == null)
        {
            return NotFound(new { error = "Task not found or access denied" });
        }

        return Ok(new GenerateDocumentResponseDto
        {
            TaskId = task.Id,
            Phase = task.Phase,
            GeneratedContent = task.Content ?? "",
            ModelUsed = "rotação-inteligente",  // Rotaciona entre gemini-2.0-flash-exp, gemini-2.5-flash, gemini-flash-1.5
            TokensUsed = (task.Content ?? "").Length / 4,
            Status = task.Status
        });
    }

    /// <summary>
    /// Exporta todos os documentos do projeto em formato PDF
    /// </summary>
    [HttpGet("export/pdf")]
    public async Task<IActionResult> ExportToPdf(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Exporting documents to PDF for project {ProjectId}", projectId);

        var pdfBytes = await _pdfExportService.ExportProjectDocumentsAsync(projectId, userId);

        if (pdfBytes == null)
        {
            return NotFound(new { error = "No documents found for this project or access denied" });
        }

        var fileName = $"Relatorio_Projeto_{projectId}_{DateTime.Now:yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", fileName);
    }

    /// <summary>
    /// Exporta um documento específico (por fase) em formato PDF
    /// </summary>
    [HttpGet("export/pdf/{phase}")]
    public async Task<IActionResult> ExportSinglePhaseToPdf(
        Guid projectId,
        string phase,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Exporting single document for project {ProjectId}, phase {Phase}", projectId, phase);

        var pdfBytes = await _pdfExportService.ExportSinglePhaseDocumentAsync(projectId, userId, phase);

        if (pdfBytes == null)
        {
            return NotFound(new { error = $"Document for phase {phase} not found or access denied" });
        }

        var fileName = $"Relatorio_{phase}_{projectId}_{DateTime.Now:yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", fileName);
    }

}

/// <summary>
/// DTO para refinamento de documento
/// </summary>
public class RefineDocumentDto
{
    public string Feedback { get; set; } = null!;
}
