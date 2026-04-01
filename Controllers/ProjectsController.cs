using IdeorAI.Model.DTOs;
using IdeorAI.Model.Entities;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IdeorAI.Controllers;

/// <summary>
/// Controller para gerenciamento de projetos
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectService projectService,
        ILogger<ProjectsController> logger)
    {
        _projectService = projectService;
        _logger = logger;
    }

    /// <summary>
    /// Obtém todos os projetos do usuário autenticado
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProjectResponseDto>>> GetUserProjects(
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Getting projects for user {UserId}", userId);

        var projects = await _projectService.GetUserProjectsAsync(userId);

        var response = projects.Select(p => MapToDto(p)).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Obtém um projeto específico por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectResponseDto>> GetProject(
        Guid id,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Getting project {ProjectId} for user {UserId}", id, userId);

        var project = await _projectService.GetByIdAsync(id, userId);

        if (project == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        return Ok(MapToDto(project));
    }

    /// <summary>
    /// Cria um novo projeto
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProjectResponseDto>> CreateProject(
        [FromBody] CreateProjectDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Creating project for user {UserId}", userId);

        var project = new Project
        {
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            ProductStructure = dto.ProductStructure,
            TargetAudience = dto.TargetAudience,
            Score = 0,
            Valuation = 250.00m,
            CurrentPhase = "fase1",
            ProgressBreakdown = JsonDocument.Parse("{}")
        };

        var createdProject = await _projectService.CreateAsync(project, userId);

        return CreatedAtAction(
            nameof(GetProject),
            new { id = createdProject.Id },
            MapToDto(createdProject));
    }

    /// <summary>
    /// Atualiza um projeto existente
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ProjectResponseDto>> UpdateProject(
        Guid id,
        [FromBody] UpdateProjectDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Updating project {ProjectId} for user {UserId}", id, userId);

        var updatedProject = await _projectService.UpdateAsync(id, userId, project =>
        {
            if (dto.Name != null) project.Name = dto.Name;
            if (dto.Description != null) project.Description = dto.Description;
            if (dto.Category != null) project.Category = dto.Category;
            if (dto.ProductStructure != null) project.ProductStructure = dto.ProductStructure;
            if (dto.TargetAudience != null) project.TargetAudience = dto.TargetAudience;
            if (dto.Score.HasValue) project.Score = dto.Score.Value;
            if (dto.Valuation.HasValue) project.Valuation = dto.Valuation.Value;
        });

        if (updatedProject == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        return Ok(MapToDto(updatedProject));
    }

    /// <summary>
    /// Deleta um projeto
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(
        Guid id,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Deleting project {ProjectId} for user {UserId}", id, userId);

        var success = await _projectService.DeleteAsync(id, userId);

        if (!success)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        return NoContent();
    }

    /// <summary>
    /// Muda a fase de um projeto (ex: fase1 -> fase2)
    /// </summary>
    [HttpPut("{id}/phase")]
    public async Task<ActionResult<ProjectResponseDto>> ChangePhase(
        Guid id,
        [FromBody] ChangePhaseDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Changing phase of project {ProjectId} to {NewPhase}", id, dto.NewPhase);

        var updatedProject = await _projectService.ChangePhaseAsync(id, userId, dto.NewPhase);

        if (updatedProject == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        return Ok(MapToDto(updatedProject));
    }

    /// <summary>
    /// Atualiza o progresso de um projeto
    /// </summary>
    [HttpPut("{id}/progress")]
    public async Task<ActionResult<ProjectResponseDto>> UpdateProgress(
        Guid id,
        [FromBody] UpdateProgressDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Updating progress for project {ProjectId}", id);

        var updatedProject = await _projectService.UpdateProgressAsync(id, userId, dto.Progress);

        if (updatedProject == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        return Ok(MapToDto(updatedProject));
    }

    /// <summary>
    /// Mapeia entity para DTO
    /// </summary>
    private ProjectResponseDto MapToDto(Project project)
    {
        object? progressBreakdown = null;
        try
        {
            progressBreakdown = JsonSerializer.Deserialize<Dictionary<string, object>>(
                project.ProgressBreakdown.RootElement.GetRawText());
        }
        catch
        {
            progressBreakdown = new Dictionary<string, object>();
        }

        return new ProjectResponseDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Score = project.Score,
            Valuation = project.Valuation,
            CurrentPhase = project.CurrentPhase,
            Category = project.Category,
            ProductStructure = project.ProductStructure,
            TargetAudience = project.TargetAudience,
            GeneratedOptions = project.GeneratedOptions,
            ProgressBreakdown = progressBreakdown,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            TasksCount = project.Tasks?.Count ?? 0
        };
    }
}
