using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de gerenciamento de etapas (stages) dos projetos
/// Implementação com Supabase Client
/// </summary>
public class StageService : IStageService
{
    private readonly Supabase.Client _supabase;
    private readonly IProjectService _projectService;
    private readonly IBackgroundTaskRunner _bg;
    private readonly IServiceScopeFactory _scopeFactory;
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
        Supabase.Client supabase,
        IProjectService projectService,
        IBackgroundTaskRunner backgroundTaskRunner,
        IServiceScopeFactory scopeFactory,
        ILogger<StageService> logger)
    {
        _supabase = supabase;
        _projectService = projectService;
        _bg = backgroundTaskRunner;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Recalcula IVO + Score SÍNCRONO inline. Antes era fire-and-forget,
    /// mas no Render free tier o container hiberna e mata tasks em background.
    /// Como o IVO agora é mecânico (sem LLM), o overhead é < 200ms — vale a
    /// garantia de execução.
    /// </summary>
    private async Task EnqueueIvoAndScoreAsync(Guid projectId, int? stageNumber, string content)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ivo = scope.ServiceProvider.GetRequiredService<IIvoService>();
            var score = scope.ServiceProvider.GetRequiredService<IScoreService>();

            if (stageNumber.HasValue && !string.IsNullOrWhiteSpace(content))
                await ivo.EvaluateStageAsync(projectId.ToString(), stageNumber.Value, content);

            await ivo.RecalculateAndPersistAsync(projectId.ToString());
            await score.CalculateAndPersistAsync(projectId.ToString());
        }
        catch (Exception ex)
        {
            // Não rethrow — falha no recalc não deve quebrar o response da task
            _logger.LogError(ex, "Falha ao recalcular IVO/Score para project {ProjectId} stage {Stage}",
                projectId, stageNumber);
        }
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

        var model = new TaskModel
        {
            Id = task.Id.ToString(),
            ProjectId = task.ProjectId.ToString(),
            Title = task.Title,
            Description = task.Description,
            Phase = task.Phase,
            Content = task.Content,
            Status = task.Status,
            EvaluationResult = task.EvaluationResult != null
                ? JToken.Parse(task.EvaluationResult.RootElement.GetRawText())
                : null,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            // Navigation properties set to null to avoid schema cache issues
            Project = null,
            IaEvaluations = null
        };

        await _supabase
            .From<TaskModel>()
            .Insert(model);

        _logger.LogInformation("Task {TaskId} created successfully", task.Id);

        // Recalcular IVO + Score sequencialmente quando a task vem evaluated
        if (string.Equals(task.Status, "evaluated", StringComparison.OrdinalIgnoreCase))
        {
            await EnqueueIvoAndScoreAsync(projectId, ParseStageNumber(task.Phase), task.Content ?? "");
        }

        return task;
    }

    public async Task<ProjectTask?> GetTaskByIdAsync(Guid taskId, Guid userId)
    {
        _logger.LogInformation("Getting task {TaskId} for user {UserId}", taskId, userId);

        try
        {
            var response = await _supabase
                .From<TaskModel>()
                .Select("*, project:projects(*)")
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, taskId.ToString())
                .Single();

            if (response == null)
            {
                _logger.LogWarning("Task {TaskId} not found", taskId);
                return null;
            }

            // Validar ownership do projeto
            if (response.Project != null && response.Project.OwnerId != userId.ToString())
            {
                _logger.LogWarning("User {UserId} not authorized for task {TaskId}", userId, taskId);
                return null;
            }

            return MapTaskToEntity(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task {TaskId}", taskId);
            return null;
        }
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

        try
        {
            var response = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Order("phase", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Get();

            return response.Models.Select(MapTaskToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tasks for project {ProjectId}", projectId);
            return new List<ProjectTask>();
        }
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

        var model = new TaskModel
        {
            Id = task.Id.ToString(),
            ProjectId = task.ProjectId.ToString(),
            Title = task.Title,
            Description = task.Description,
            Phase = task.Phase,
            Content = task.Content,
            Status = task.Status,
            EvaluationResult = task.EvaluationResult != null
                ? JToken.Parse(task.EvaluationResult.RootElement.GetRawText())
                : null,
            UpdatedAt = task.UpdatedAt
        };

        await _supabase
            .From<TaskModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, taskId.ToString())
            .Update(model);

        _logger.LogInformation("Task {TaskId} updated successfully", taskId);

        // Recalcular score e IVO quando task é atualizada com status evaluated (ex: regeneração)
        if (string.Equals(task.Status, "evaluated", StringComparison.OrdinalIgnoreCase))
        {
            await EnqueueIvoAndScoreAsync(task.ProjectId, ParseStageNumber(task.Phase), task.Content ?? "");
        }

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

        var updated = await UpdateTaskAsync(taskId, userId, task =>
        {
            task.Status = newStatus;
        });

        // Recalcular score quando task é avaliada (scope dedicado)
        if (updated != null && newStatus == "evaluated")
        {
            var pid = updated.ProjectId;
            _bg.Run(async (sp, _) =>
            {
                var score = sp.GetRequiredService<IScoreService>();
                await score.CalculateAndPersistAsync(pid.ToString());
            }, $"score-{pid}");
        }

        return updated;
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
            try
            {
                var response = await _supabase
                    .From<TaskModel>()
                    .Select("id")
                    .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                    .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "evaluated")
                    .Get();

                return response.Models.Count >= 7; // Todas as 7 etapas completas
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if can advance phase for project {ProjectId}", projectId);
                return false;
            }
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

    // Helper: extrai número da etapa de "etapa1" → 1, "etapa2" → 2, etc.
    private static int? ParseStageNumber(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase)) return null;
        var digits = new string(phase.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var num) ? num : null;
    }

    // Helper para converter TaskModel (Supabase) para ProjectTask (Entity)
    private ProjectTask MapTaskToEntity(TaskModel model)
    {
        return new ProjectTask
        {
            Id = Guid.Parse(model.Id),
            ProjectId = Guid.Parse(model.ProjectId),
            Title = model.Title,
            Description = model.Description,
            Phase = model.Phase,
            Content = model.Content,
            Status = model.Status,
            EvaluationResult = model.EvaluationResult != null
                ? JsonDocument.Parse(model.EvaluationResult.ToString())
                : null,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }
}
