using IdeorAI.Model.DTOs;
using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
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
    private readonly IScoreService _scoreService;
    private readonly IIvoService _ivoService;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectService projectService,
        IScoreService scoreService,
        IIvoService ivoService,
        Supabase.Client supabase,
        ILogger<ProjectsController> logger)
    {
        _projectService = projectService;
        _scoreService = scoreService;
        _ivoService = ivoService;
        _supabase = supabase;
        _logger = logger;
    }

    private async Task<string> GetMyRoleAsync(Guid projectId, Guid userId, Guid ownerId)
    {
        if (ownerId == userId) return "owner";

        var res = await _supabase
            .From<ProjectMemberModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "accepted")
            .Get();

        return res.Models.FirstOrDefault()?.Role ?? "viewer";
    }

    /// <summary>
    /// Obtém todos os projetos do usuário autenticado (próprios + compartilhados com myRole)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProjectResponseDto>>> GetUserProjects(
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Getting projects for user {UserId}", userId);

        var projects = await _projectService.GetUserProjectsAsync(userId);

        // Pré-buscar memberships aceitas para evitar N+1
        var membershipsRes = await _supabase
            .From<ProjectMemberModel>()
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "accepted")
            .Get();
        var membershipMap = membershipsRes.Models
            .ToDictionary(m => m.ProjectId, m => m.Role);

        var response = projects.Select(p =>
        {
            string myRole = p.OwnerId == userId
                ? "owner"
                : membershipMap.GetValueOrDefault(p.Id.ToString(), "viewer");
            return MapToDto(p, myRole);
        }).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Obtém um projeto específico por ID (inclui myRole: owner | editor | viewer)
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

        var myRole = await GetMyRoleAsync(id, userId, project.OwnerId);
        return Ok(MapToDto(project, myRole));
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
    /// Deleta um projeto (somente o owner pode deletar — membros recebem 403)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(
        Guid id,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Deleting project {ProjectId} for user {UserId}", id, userId);

        // Verificar se o projeto existe e se o caller é o owner
        var project = await _projectService.GetByIdAsync(id, userId);
        if (project == null)
            return NotFound(new { error = "Project not found or access denied" });

        if (project.OwnerId != userId)
            return StatusCode(403, new { error = "Apenas o dono do projeto pode excluí-lo." });

        var success = await _projectService.DeleteAsync(id, userId);
        if (!success)
            return NotFound(new { error = "Project not found or access denied" });

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
    /// Recalcula e persiste o score de um projeto com base nas tasks avaliadas
    /// </summary>
    [HttpPost("{id}/recalculate-score")]
    public async Task<ActionResult<object>> RecalculateScore(
        Guid id,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("Recalculating score for project {ProjectId}", id);

        var project = await _projectService.GetByIdAsync(id, userId);
        if (project == null)
        {
            return NotFound(new { error = "Project not found or access denied" });
        }

        var score = await _scoreService.CalculateAndPersistAsync(id.ToString());

        return Ok(new { projectId = id, score });
    }

    /// <summary>
    /// Retorna dados IVO do projeto sem recalcular
    /// </summary>
    [HttpGet("{id}/ivo")]
    public async Task<ActionResult<IvoDataDto>> GetIvo(
        Guid id,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var project = await _projectService.GetByIdAsync(id, userId);
        if (project == null)
            return NotFound(new { error = "Project not found or access denied" });

        var ivo = await _ivoService.GetIvoDataAsync(id.ToString());
        if (ivo == null)
            return NotFound(new { error = "IVO data not available" });

        return Ok(ivo);
    }

    /// <summary>
    /// Recalcula e persiste o IVO de um projeto.
    /// Com ?full=true, re-avalia todas as etapas via Gemini (útil para migrar dados existentes).
    /// </summary>
    [HttpPost("{id}/recalculate-ivo")]
    public async Task<ActionResult<IvoDataDto>> RecalculateIvo(
        Guid id,
        [FromHeader(Name = "x-user-id")] Guid userId,
        [FromQuery] bool full = false)
    {
        _logger.LogInformation("Recalculating IVO for project {ProjectId} (full={Full})", id, full);

        var project = await _projectService.GetByIdAsync(id, userId);
        if (project == null)
            return NotFound(new { error = "Project not found or access denied" });

        if (full)
            await _ivoService.ReevaluateAllStagesAsync(id.ToString());
        else
            await _ivoService.RecalculateAndPersistAsync(id.ToString());

        var ivo = await _ivoService.GetIvoDataAsync(id.ToString());
        return Ok(ivo);
    }

    private ProjectResponseDto MapToDto(Project project, string myRole = "owner")
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
            TasksCount = project.Tasks?.Count ?? 0,
            MyRole = myRole
        };
    }
}
