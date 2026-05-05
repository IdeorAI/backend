using IdeorAI.Model.SupabaseModels;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

/// <summary>
/// Marcos individuais de projeto. Cada marco vale 4 pts no score (máx 5 marcos = 20 pts).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/milestones")]
public class ProjectMilestonesController : ControllerBase
{
    private readonly Supabase.Client _supabase;
    private readonly IProjectService _projectService;
    private readonly IScoreService _scoreService;
    private readonly ILogger<ProjectMilestonesController> _logger;

    public ProjectMilestonesController(
        Supabase.Client supabase,
        IProjectService projectService,
        IScoreService scoreService,
        ILogger<ProjectMilestonesController> logger)
    {
        _supabase = supabase;
        _projectService = projectService;
        _scoreService = scoreService;
        _logger = logger;
    }

    public record MilestoneRequest(string MilestoneKey, string Title, string? Description);

    [HttpGet]
    public async Task<IActionResult> List(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (project == null) return NotFound();

        var result = await _supabase
            .From<ProjectMilestoneModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Order("achieved_at", Supabase.Postgrest.Constants.Ordering.Ascending)
            .Get();

        return Ok(result.Models);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId,
        [FromBody] MilestoneRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.MilestoneKey) || string.IsNullOrWhiteSpace(body.Title))
            return BadRequest(new { error = "milestoneKey e title são obrigatórios" });

        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (project == null) return NotFound();
        if (project.OwnerId != userId) return Forbid();

        // Garantir unicidade da chave
        var existing = await _supabase
            .From<ProjectMilestoneModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("milestone_key", Supabase.Postgrest.Constants.Operator.Equals, body.MilestoneKey)
            .Get();

        if (existing.Models.Count > 0)
            return Conflict(new { error = "Marco já registrado para esse projeto", milestone = existing.Models[0] });

        var record = new ProjectMilestoneModel
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId.ToString(),
            UserId = userId.ToString(),
            MilestoneKey = body.MilestoneKey.Trim(),
            Title = body.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            AchievedAt = DateTime.UtcNow,
        };

        await _supabase.From<ProjectMilestoneModel>().Insert(record);

        _ = _scoreService.CalculateAndPersistAsync(projectId.ToString());

        _logger.LogInformation("Milestone {Key} created for project {ProjectId}", record.MilestoneKey, projectId);
        return Created($"/api/projects/{projectId}/milestones/{record.Id}", record);
    }

    [HttpDelete("{milestoneId:guid}")]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid milestoneId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (project == null) return NotFound();
        if (project.OwnerId != userId) return Forbid();

        await _supabase
            .From<ProjectMilestoneModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, milestoneId.ToString())
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Delete();

        _ = _scoreService.CalculateAndPersistAsync(projectId.ToString());

        return NoContent();
    }
}
