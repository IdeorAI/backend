using IdeorAI.Data;
using IdeorAI.Model.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de gerenciamento de etapas (stages) dos projetos
/// </summary>
public class StageService : IStageService
{
    private readonly IdeorDbContext _context;
    private readonly IProjectService _projectService;
    private readonly ILogger<StageService> _logger;

    // Definição das 7 etapas da Fase Projeto
    private static readonly string[] Phase2Stages = new[]
    {
        "etapa1", // Problema e Oportunidade
        "etapa2", // Pesquisa de Mercado
        "etapa3", // Proposta de Valor
        "etapa4", // Modelo de Negócio
        "etapa5", // MVP
        "etapa6", // Equipe Mínima
        "etapa7"  // Pitch Deck + Plano + Resumo
    };

    public StageService(
        IdeorDbContext context,
        IProjectService projectService,
        ILogger<StageService> logger)
    {
        _context = context;
        _projectService = projectService;
        _logger = logger;
    }

    public async Task<ProjectTask?> CreateTaskAsync(Guid projectId, Guid userId, ProjectTask task)
    {
        _logger.LogInformation("Creating task for project {ProjectId}", projectId);

        // Validar ownership do projeto
        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (project == null)
        {
            _logger.LogWarning("User {UserId} not authorized for project {ProjectId}", userId, projectId);
            return null;
        }

        task.Id = Guid.NewGuid();
        task.ProjectId = projectId;
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        _context.Tasks.Add(task);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Task {TaskId} created successfully", task.Id);

        return task;
    }

    public async Task<ProjectTask?> GetTaskByIdAsync(Guid taskId, Guid userId)
    {
        _logger.LogInformation("Getting task {TaskId} for user {UserId}", taskId, userId);

        var task = await _context.Tasks
            .Include(t => t.Project)
            .Include(t => t.IaEvaluations)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", taskId);
            return null;
        }

        // Validar ownership do projeto
        if (task.Project.OwnerId != userId)
        {
            _logger.LogWarning("User {UserId} not authorized for task {TaskId}", userId, taskId);
            return null;
        }

        return task;
    }

    public async Task<List<ProjectTask>?> GetProjectTasksAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Getting tasks for project {ProjectId}", projectId);

        // Validar ownership do projeto
        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (project == null)
        {
            return null;
        }

        var tasks = await _context.Tasks
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Phase)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        return tasks;
    }

    public async Task<ProjectTask?> UpdateTaskAsync(Guid taskId, Guid userId, Action<ProjectTask> updateAction)
    {
        _logger.LogInformation("Updating task {TaskId}", taskId);

        var task = await GetTaskByIdAsync(taskId, userId);
        if (task == null)
        {
            return null;
        }

        updateAction(task);
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Task {TaskId} updated successfully", taskId);

        return task;
    }

    public async Task<ProjectTask?> ChangeTaskStatusAsync(Guid taskId, Guid userId, string newStatus)
    {
        _logger.LogInformation("Changing status of task {TaskId} to {NewStatus}", taskId, newStatus);

        // Validar status
        var validStatuses = new[] { "draft", "submitted", "evaluated" };
        if (!validStatuses.Contains(newStatus))
        {
            _logger.LogWarning("Invalid status: {NewStatus}", newStatus);
            return null;
        }

        return await UpdateTaskAsync(taskId, userId, task =>
        {
            task.Status = newStatus;
        });
    }

    public async Task<bool> CanAdvanceToNextPhaseAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Checking if project {ProjectId} can advance to next phase", projectId);

        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (project == null)
        {
            return false;
        }

        // Se está na fase1, pode avançar (critério simples por enquanto)
        if (project.CurrentPhase == "fase1")
        {
            // Pode adicionar validações (ex: ter nome, categoria, etc)
            return !string.IsNullOrWhiteSpace(project.Name);
        }

        // Se está na fase2, verificar se todas as 7 etapas estão evaluated
        if (project.CurrentPhase == "fase2")
        {
            var evaluatedTasks = await _context.Tasks
                .Where(t => t.ProjectId == projectId && t.Status == "evaluated")
                .CountAsync();

            return evaluatedTasks >= 7; // Todas as 7 etapas completas
        }

        return false;
    }

    public async Task<string?> GetNextAvailableStageAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Getting next available stage for project {ProjectId}", projectId);

        var tasks = await GetProjectTasksAsync(projectId, userId);
        if (tasks == null)
        {
            return null;
        }

        // Verificar quais etapas já existem
        var existingStages = tasks
            .Where(t => Phase2Stages.Contains(t.Phase))
            .Select(t => t.Phase)
            .ToHashSet();

        // Retornar a primeira etapa que não existe
        foreach (var stage in Phase2Stages)
        {
            if (!existingStages.Contains(stage))
            {
                return stage;
            }
        }

        // Todas as etapas já foram criadas
        // Verificar se alguma ainda não foi avaliada
        var unevaluatedStage = tasks
            .Where(t => Phase2Stages.Contains(t.Phase) && t.Status != "evaluated")
            .OrderBy(t => Array.IndexOf(Phase2Stages, t.Phase))
            .FirstOrDefault();

        if (unevaluatedStage != null)
        {
            return unevaluatedStage.Phase;
        }

        // Todas completas
        return null;
    }
}
