using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de gerenciamento de projetos com validação de ownership
/// Implementação com Supabase Client
/// </summary>
public class ProjectService : IProjectService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(Supabase.Client supabase, ILogger<ProjectService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    public async Task<Project?> GetByIdAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Getting project {ProjectId} for user {UserId}", projectId, userId);

        try
        {
            var response = await _supabase
                .From<ProjectModel>()
                .Select("*, tasks(*)")
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Filter("owner_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Single();

            if (response == null)
            {
                _logger.LogWarning("Project {ProjectId} not found or user {UserId} not authorized", projectId, userId);
                return null;
            }

            return MapToEntity(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project {ProjectId}", projectId);
            return null;
        }
    }

    public async Task<List<Project>> GetUserProjectsAsync(Guid userId, bool includeDeleted = false)
    {
        _logger.LogInformation("Getting all projects for user {UserId}", userId);

        try
        {
            var response = await _supabase
                .From<ProjectModel>()
                .Select("*, tasks(*)")
                .Filter("owner_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Order("updated_at", Supabase.Postgrest.Constants.Ordering.Descending)
                .Get();

            var projects = response.Models.Select(MapToEntity).ToList();

            _logger.LogInformation("Found {Count} projects for user {UserId}", projects.Count, userId);

            return projects;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting projects for user {UserId}", userId);
            return new List<Project>();
        }
    }

    public async Task<Project> CreateAsync(Project project, Guid userId)
    {
        _logger.LogInformation("Creating new project for user {UserId}", userId);

        project.OwnerId = userId;
        project.Id = Guid.NewGuid();
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        if (project.ProgressBreakdown == null)
        {
            project.ProgressBreakdown = JsonDocument.Parse("{}");
        }

        var model = new ProjectModel
        {
            Id = project.Id.ToString(),
            OwnerId = project.OwnerId.ToString(),
            Name = project.Name,
            Description = project.Description,
            Score = project.Score,
            Valuation = project.Valuation,
            ProgressBreakdown = project.ProgressBreakdown?.RootElement,
            CurrentPhase = project.CurrentPhase,
            Category = project.Category,
            GeneratedOptions = project.GeneratedOptions,
            ProductStructure = project.ProductStructure,
            TargetAudience = project.TargetAudience,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };

        var response = await _supabase
            .From<ProjectModel>()
            .Insert(model);

        _logger.LogInformation("Project {ProjectId} created successfully", project.Id);

        return project;
    }

    public async Task<Project?> UpdateAsync(Guid projectId, Guid userId, Action<Project> updateAction)
    {
        _logger.LogInformation("Updating project {ProjectId} for user {UserId}", projectId, userId);

        var project = await GetByIdAsync(projectId, userId);

        if (project == null)
        {
            return null;
        }

        updateAction(project);
        project.UpdatedAt = DateTime.UtcNow;

        var model = new ProjectModel
        {
            Id = project.Id.ToString(),
            OwnerId = project.OwnerId.ToString(),
            Name = project.Name,
            Description = project.Description,
            Score = project.Score,
            Valuation = project.Valuation,
            ProgressBreakdown = project.ProgressBreakdown?.RootElement,
            CurrentPhase = project.CurrentPhase,
            Category = project.Category,
            GeneratedOptions = project.GeneratedOptions,
            ProductStructure = project.ProductStructure,
            TargetAudience = project.TargetAudience,
            UpdatedAt = project.UpdatedAt
        };

        await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Update(model);

        _logger.LogInformation("Project {ProjectId} updated successfully", projectId);

        return project;
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Deleting project {ProjectId} for user {UserId}", projectId, userId);

        var project = await GetByIdAsync(projectId, userId);

        if (project == null)
        {
            return false;
        }

        await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Delete();

        _logger.LogInformation("Project {ProjectId} deleted successfully", projectId);

        return true;
    }

    public async Task<Project?> ChangePhaseAsync(Guid projectId, Guid userId, string newPhase)
    {
        _logger.LogInformation("Changing phase of project {ProjectId} to {NewPhase}", projectId, newPhase);

        return await UpdateAsync(projectId, userId, project =>
        {
            project.CurrentPhase = newPhase;
        });
    }

    public async Task<Project?> UpdateProgressAsync(Guid projectId, Guid userId, object progressData)
    {
        _logger.LogInformation("Updating progress for project {ProjectId}", projectId);

        return await UpdateAsync(projectId, userId, project =>
        {
            var json = JsonSerializer.Serialize(progressData);
            project.ProgressBreakdown = JsonDocument.Parse(json);
        });
    }

    // Helper para converter ProjectModel (Supabase) para Project (Entity)
    private Project MapToEntity(ProjectModel model)
    {
        var project = new Project
        {
            Id = Guid.Parse(model.Id),
            OwnerId = Guid.Parse(model.OwnerId),
            Name = model.Name,
            Description = model.Description,
            Score = model.Score,
            Valuation = model.Valuation,
            ProgressBreakdown = model.ProgressBreakdown.HasValue
                ? JsonDocument.Parse(model.ProgressBreakdown.Value.GetRawText())
                : JsonDocument.Parse("{}"),
            CurrentPhase = model.CurrentPhase,
            Category = model.Category,
            GeneratedOptions = model.GeneratedOptions,
            ProductStructure = model.ProductStructure,
            TargetAudience = model.TargetAudience,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        // Mapear tasks se existirem
        if (model.Tasks != null && model.Tasks.Count > 0)
        {
            project.Tasks = model.Tasks.Select(t => new ProjectTask
            {
                Id = Guid.Parse(t.Id),
                ProjectId = Guid.Parse(t.ProjectId),
                Title = t.Title,
                Description = t.Description,
                Phase = t.Phase,
                Content = t.Content,
                Status = t.Status,
                EvaluationResult = t.EvaluationResult.HasValue
                    ? JsonDocument.Parse(t.EvaluationResult.Value.GetRawText())
                    : null,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            }).ToList();
        }

        return project;
    }
}
