using IdeorAI.Model.DTOs;
using IdeorAI.Model.Entities;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IdeorAI.Controllers;

/// <summary>
/// Controller para gerenciamento de tasks/etapas dos projetos
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/tasks")]
public class TasksController : ControllerBase
{
    private readonly IStageService _stageService;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        IStageService stageService,
        ILogger<TasksController> logger)
    {
        _stageService = stageService;
        _logger = logger;
    }

    /// <summary>
    /// Obtém todas as tasks de um projeto
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TaskResponseDto>>> GetProjectTasks(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Getting tasks for project {ProjectId}", projectId);

        var tasks = await _stageService.GetProjectTasksAsync(projectId, userId);

        if (tasks == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        var response = tasks.Select(t => MapToDto(t)).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Obtém uma task específica por ID
    /// </summary>
    [HttpGet("~/api/tasks/{taskId}")]
    public async Task<ActionResult<TaskResponseDto>> GetTask(
        Guid taskId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Getting task {TaskId}", taskId);

        var task = await _stageService.GetTaskByIdAsync(taskId, userId);

        if (task == null)
        {
            return NotFound(new { error = "Task not found or access denied" });
        }

        return Ok(MapToDto(task));
    }

    /// <summary>
    /// Cria uma nova task para um projeto
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TaskResponseDto>> CreateTask(
        Guid projectId,
        [FromBody] CreateTaskDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Creating task for project {ProjectId}", projectId);

        var task = new ProjectTask
        {
            Title = dto.Title,
            Description = dto.Description,
            Phase = dto.Phase,
            Content = dto.Content,
            Status = "draft"
        };

        var createdTask = await _stageService.CreateTaskAsync(projectId, userId, task);

        if (createdTask == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        return CreatedAtAction(
            nameof(GetTask),
            new { taskId = createdTask.Id },
            MapToDto(createdTask));
    }

    /// <summary>
    /// Atualiza uma task existente
    /// </summary>
    [HttpPut("~/api/tasks/{taskId}")]
    public async Task<ActionResult<TaskResponseDto>> UpdateTask(
        Guid taskId,
        [FromBody] UpdateTaskDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Updating task {TaskId}", taskId);

        var updatedTask = await _stageService.UpdateTaskAsync(taskId, userId, task =>
        {
            if (dto.Title != null) task.Title = dto.Title;
            if (dto.Description != null) task.Description = dto.Description;
            if (dto.Content != null) task.Content = dto.Content;
        });

        if (updatedTask == null)
        {
            return NotFound(new { error = "Task not found or access denied" });
        }

        return Ok(MapToDto(updatedTask));
    }

    /// <summary>
    /// Muda o status de uma task (draft -> submitted -> evaluated)
    /// </summary>
    [HttpPut("~/api/tasks/{taskId}/status")]
    public async Task<ActionResult<TaskResponseDto>> ChangeTaskStatus(
        Guid taskId,
        [FromBody] ChangeTaskStatusDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Changing status of task {TaskId} to {NewStatus}", taskId, dto.NewStatus);

        var updatedTask = await _stageService.ChangeTaskStatusAsync(taskId, userId, dto.NewStatus);

        if (updatedTask == null)
        {
            return BadRequest(new { error = "Invalid status or task not found" });
        }

        return Ok(MapToDto(updatedTask));
    }

    /// <summary>
    /// Obtém a próxima etapa disponível para um projeto
    /// </summary>
    [HttpGet("~/api/projects/{projectId}/next-stage")]
    public async Task<ActionResult<object>> GetNextStage(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Getting next stage for project {ProjectId}", projectId);

        var nextStage = await _stageService.GetNextAvailableStageAsync(projectId, userId);

        if (nextStage == null)
        {
            return Ok(new { nextStage = (string?)null, message = "All stages completed" });
        }

        return Ok(new { nextStage, message = $"Next available stage: {nextStage}" });
    }

    /// <summary>
    /// Verifica se o projeto pode avançar para a próxima fase
    /// </summary>
    [HttpGet("~/api/projects/{projectId}/can-advance")]
    public async Task<ActionResult<object>> CanAdvance(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Checking if project {ProjectId} can advance", projectId);

        var canAdvance = await _stageService.CanAdvanceToNextPhaseAsync(projectId, userId);

        return Ok(new { canAdvance, projectId });
    }

    /// <summary>
    /// Mapeia entity para DTO
    /// </summary>
    private TaskResponseDto MapToDto(ProjectTask task)
    {
        object? evaluationResult = null;
        if (task.EvaluationResult != null)
        {
            try
            {
                evaluationResult = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    task.EvaluationResult.RootElement.GetRawText());
            }
            catch
            {
                evaluationResult = null;
            }
        }

        return new TaskResponseDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Phase = task.Phase,
            Content = task.Content,
            Status = task.Status,
            EvaluationResult = evaluationResult,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            EvaluationsCount = task.IaEvaluations?.Count ?? 0
        };
    }
}
