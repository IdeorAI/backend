using IdeorAI.Model.DTOs;
using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IGoPivotService _goPivotService;
    private readonly IProjectService _projectService;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentGenerationService documentService,
        IPdfExportService pdfExportService,
        IStageService stageService,
        IStageSummaryService stageSummaryService,
        IGoPivotService goPivotService,
        IProjectService projectService,
        Supabase.Client supabase,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _pdfExportService = pdfExportService;
        _stageService = stageService;
        _stageSummaryService = stageSummaryService;
        _goPivotService = goPivotService;
        _projectService = projectService;
        _supabase = supabase;
        _logger = logger;
    }

    // Retorna (allowed, effectiveUserId). Tokens debitados do owner (spec 007 T-019).
    private async Task<(bool allowed, Guid effectiveUserId)> RequireEditorAsync(Guid projectId, Guid callerId)
    {
        var project = await _projectService.GetByIdAsync(projectId, callerId);
        if (project == null) return (false, Guid.Empty);

        if (project.OwnerId == callerId) return (true, callerId);

        var memberRes = await _supabase
            .From<ProjectMemberModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, callerId.ToString())
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "accepted")
            .Filter("role", Supabase.Postgrest.Constants.Operator.Equals, "editor")
            .Get();

        if (memberRes.Models.Count > 0)
            return (true, project.OwnerId); // tokens debitados do owner

        return (false, Guid.Empty);
    }

    /// <summary>
    /// Gera um documento para uma etapa específica (requer role owner ou editor)
    /// </summary>
    [HttpPost("generate")]
    [EnableRateLimiting("ai-generation")]
    public async Task<ActionResult<GenerateDocumentResponseDto>> GenerateDocument(
        Guid projectId,
        [FromBody] GenerateDocumentDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var (allowed, effectiveUserId) = await RequireEditorAsync(projectId, userId);
        if (!allowed)
            return StatusCode(403, new { error = "Você precisa ser editor ou dono do projeto para gerar documentos." });

        _logger.LogInformation("Generating document for project {ProjectId}, stage {Stage} (effectiveUser={EffectiveUserId})",
            projectId, dto.Phase, effectiveUserId);

        var task = await _documentService.GenerateDocumentAsync(
            projectId,
            effectiveUserId,
            dto.Phase,
            dto.Inputs);

        if (task == null)
        {
            return BadRequest(new { error = "Falha ao gerar documento. Se o erro persistir, aguarde 1 minuto e tente novamente (limite de requisições da IA)." });
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

        // Ao regenerar etapa2, invalidar avaliação GO/PIVOT existente
        if (string.Equals(existingTask.Phase, "etapa2", StringComparison.OrdinalIgnoreCase))
        {
            try { await _goPivotService.InvalidateAsync(existingTask.ProjectId); }
            catch (Exception ex) { _logger.LogWarning(ex, "Falha ao invalidar GO/PIVOT (continuando)"); }
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

    /// <summary>
    /// Gera PDF de uma etapa específica (Spec 018) — markdown-aware,
    /// header IdeorAI + projeto + etapa, footer paginação.
    /// </summary>
    [HttpPost("tasks/{taskId}/pdf")]
    public async Task<IActionResult> DownloadStagePdf(
        string projectId,
        string taskId,
        [FromHeader(Name = "x-user-id")] string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            var bytes = await _pdfExportService.GenerateStagePdfAsync(projectId, taskId, userId, ct);
            var contentDisposition = $"attachment; filename=\"IdeorAI-stage-{taskId}.pdf\"";
            Response.Headers["Content-Disposition"] = contentDisposition;
            return File(bytes, "application/pdf");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StagePdf] Erro gerando PDF para project {ProjectId} task {TaskId}", projectId, taskId);
            return StatusCode(500, new { error = "Erro ao gerar PDF" });
        }
    }

    /// <summary>
    /// Spec 029 — Gera Prompt Master para Vibe Coding (Lovable, v0, Bolt, etc.)
    /// Retorna Markdown pronto para copiar e colar em plataformas de desenvolvimento por IA.
    /// Requer role owner ou editor.
    /// </summary>
    [HttpGet("{projectId}/mvp-prompt")]
    public async Task<ActionResult<string>> GenerateMvpPromptMaster(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var (allowed, effectiveUserId) = await RequireEditorAsync(projectId, userId);
        if (!allowed)
        {
            return StatusCode(403, new { error = "Você precisa ser editor ou dono do projeto para exportar o Prompt Master." });
        }

        _logger.LogInformation("Generating MVP Prompt Master for project {ProjectId} (user={UserId})", projectId, userId);

        // Buscar projeto
        var project = await _projectService.GetByIdAsync(projectId, effectiveUserId);
        if (project == null)
        {
            return NotFound(new { error = "Projeto não encontrado" });
        }

        // Buscar tasks do projeto (etapas 1-5)
        var tasks = await _stageService.GetProjectTasksAsync(projectId, effectiveUserId);
        var evaluatedTasks = tasks?
            .Where(t => t.Status == "evaluated" && (t.Phase?.StartsWith("etapa") ?? false))
            .OrderBy(t => t.Phase)
            .ToList() ?? new List<ProjectTask>();

        // Gerar Prompt Master (montagem direta, sem LLM)
        var promptMaster = PromptTemplates.GenerateMvpPromptMaster(project, evaluatedTasks);

        _logger.LogInformation("MVP Prompt Master generated for project {ProjectId} ({Count} stages included)",
            projectId, evaluatedTasks.Count);

        // Retornar como texto/plain para facilitar cópia
        return Content(promptMaster, "text/plain; charset=utf-8");
    }
}

/// <summary>
/// DTO para refinamento de documento
/// </summary>
public class RefineDocumentDto
{
    public string Feedback { get; set; } = null!;
}
