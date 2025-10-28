using IdeorAI.Data;
using IdeorAI.Model.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de gerenciamento de projetos com validação de ownership
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IdeorDbContext _context;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(IdeorDbContext context, ILogger<ProjectService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Project?> GetByIdAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Getting project {ProjectId} for user {UserId}", projectId, userId);

        var project = await _context.Projects
            .Include(p => p.Owner)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found or user {UserId} not authorized", projectId, userId);
        }

        return project;
    }

    public async Task<List<Project>> GetUserProjectsAsync(Guid userId, bool includeDeleted = false)
    {
        _logger.LogInformation("Getting all projects for user {UserId}", userId);

        var query = _context.Projects
            .Include(p => p.Tasks)
            .Where(p => p.OwnerId == userId);

        // Se implementar soft delete, adicionar filtro aqui
        // if (!includeDeleted)
        // {
        //     query = query.Where(p => !p.IsDeleted);
        // }

        var projects = await query
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();

        _logger.LogInformation("Found {Count} projects for user {UserId}", projects.Count, userId);

        return projects;
    }

    public async Task<Project> CreateAsync(Project project, Guid userId)
    {
        _logger.LogInformation("Creating new project for user {UserId}", userId);

        // Garantir que o owner está correto
        project.OwnerId = userId;
        project.Id = Guid.NewGuid();
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        // Garantir que progress_breakdown não é null
        if (project.ProgressBreakdown == null)
        {
            project.ProgressBreakdown = JsonDocument.Parse("{}");
        }

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

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

        await _context.SaveChangesAsync();

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

        // Hard delete (pode implementar soft delete se necessário)
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

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
}
