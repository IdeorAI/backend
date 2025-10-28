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
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentGenerationService documentService,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
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

        return Ok(new GenerateDocumentResponseDto
        {
            TaskId = task.Id,
            Phase = task.Phase,
            GeneratedContent = task.Content ?? "",
            ModelUsed = "gemini-2.5-flash",
            TokensUsed = EstimateTokens(task.Content ?? ""),
            Status = task.Status
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

        var task = await _documentService.RegenerateDocumentAsync(taskId, userId, newInputs);

        if (task == null)
        {
            return NotFound(new { error = "Task not found or access denied" });
        }

        return Ok(new GenerateDocumentResponseDto
        {
            TaskId = task.Id,
            Phase = task.Phase,
            GeneratedContent = task.Content ?? "",
            ModelUsed = "gemini-2.5-flash",
            TokensUsed = EstimateTokens(task.Content ?? ""),
            Status = task.Status
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
            ModelUsed = "gemini-2.5-flash",
            TokensUsed = EstimateTokens(task.Content ?? ""),
            Status = task.Status
        });
    }

    private int EstimateTokens(string text)
    {
        return text.Length / 4;
    }
}

/// <summary>
/// DTO para refinamento de documento
/// </summary>
public class RefineDocumentDto
{
    public string Feedback { get; set; } = null!;
}
