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
        _logger.LogInformation("[IVO-Score] ▶ ENTRANDO project={ProjectId} stage={Stage} contentLen={Len}",
            projectId, stageNumber, content?.Length ?? 0);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ivo = scope.ServiceProvider.GetRequiredService<IIvoService>();
            var score = scope.ServiceProvider.GetRequiredService<IScoreService>();

            // ORDEM: Score primeiro, depois IVO recalc — porque IVO usa project.Score
            // como entrada (IvoScore10 = Score/10). Inverter causa lost-update.

            if (stageNumber.HasValue && !string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("[IVO-Score] → chamando EvaluateStageAsync project={ProjectId} stage={Stage}",
                    projectId, stageNumber);
                await ivo.EvaluateStageAsync(projectId.ToString(), stageNumber.Value, content);
                _logger.LogInformation("[IVO-Score] ✓ EvaluateStageAsync OK project={ProjectId}", projectId);
            }

            _logger.LogInformation("[IVO-Score] → chamando CalculateAndPersistAsync (Score) project={ProjectId}", projectId);
            await score.CalculateAndPersistAsync(projectId.ToString());
            _logger.LogInformation("[IVO-Score] ✓ Score OK project={ProjectId}", projectId);

            _logger.LogInformation("[IVO-Score] → chamando RecalculateAndPersistAsync project={ProjectId}", projectId);
            await ivo.RecalculateAndPersistAsync(projectId.ToString());
            _logger.LogInformation("[IVO-Score] ✓ RecalculateAndPersistAsync OK project={ProjectId}", projectId);

            _logger.LogInformation("[IVO-Score] ✅ SAINDO OK project={ProjectId} stage={Stage}",
                projectId, stageNumber);
        }
        catch (Exception ex)
        {
            // Não rethrow — falha no recalc não deve quebrar o response da task.
            // Stack trace completo agora aparece porque IvoService propaga as exceções.
            _logger.LogError(ex, "[IVO-Score] ❌ FALHA project={ProjectId} stage={Stage} type={ExType} msg={ExMsg}",
                projectId, stageNumber, ex.GetType().Name, ex.Message);
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

        task.ProjectId = projectId;
        task.UpdatedAt = DateTime.UtcNow;

        // Idempotência por (project_id, phase) para etapas: o Insert puro permitia
        // tasks etapaN DUPLICADAS sob auto-save concorrente (Spec 024) — causava o
        // "7/6" no roadmap. Se já existe uma task da mesma phase, ATUALIZA a mais
        // recente (regeneração) em vez de criar outra. Convive com o índice único
        // parcial em tasks(project_id, phase) WHERE phase LIKE 'etapa%'.
        var existing = (await _supabase
            .From<TaskModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("phase", Supabase.Postgrest.Constants.Operator.Equals, task.Phase)
            .Order("updated_at", Supabase.Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get()).Models.FirstOrDefault();

        if (existing != null)
        {
            task.Id = Guid.Parse(existing.Id);
            task.CreatedAt = existing.CreatedAt;
            existing.Title = task.Title;
            existing.Description = task.Description;
            existing.Content = task.Content;
            existing.Status = task.Status;
            existing.EvaluationResult = task.EvaluationResult != null
                ? JToken.Parse(task.EvaluationResult.RootElement.GetRawText())
                : null;
            existing.UpdatedAt = task.UpdatedAt;
            existing.OutdatedAt = null; // regenerar torna a etapa "atual" (Spec 023)
            existing.Project = null;
            existing.IaEvaluations = null;

            await _supabase.From<TaskModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, existing.Id)
                .Update(existing);

            _logger.LogInformation("Task {TaskId} (phase {Phase}) atualizada (idempotente)", task.Id, task.Phase);
        }
        else
        {
            task.Id = Guid.NewGuid();
            task.CreatedAt = DateTime.UtcNow;

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
        }

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
            UpdatedAt = task.UpdatedAt,
            // Evita PGRST204: o serializador tenta mandar navigation properties como colunas.
            Project = null,
            IaEvaluations = null,
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

        // UpdateTaskAsync já dispara EnqueueIvoAndScoreAsync (sync) quando status="evaluated".
        // Removido o _bg.Run duplicado que recalculava Score em background — gerava
        // race condition (lost-update concorrente) e era killado no Render free tier.
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
    /// <summary>
    /// Spec 023: marca como DESATUALIZADAS (outdated_at = now) as etapas posteriores
    /// (índice de phase &gt; stageIndex) que já estão concluídas (status='evaluated')
    /// e ainda não foram marcadas (outdated_at IS NULL). Operação SOMENTE de coluna —
    /// NÃO chama EnqueueIvoAndScoreAsync nem qualquer recálculo. Tolerante a falha
    /// parcial (loga e segue). Retorna quantas etapas foram marcadas.
    /// </summary>
    public async Task<int> MarkLaterStagesOutdatedAsync(Guid projectId, int stageIndex)
    {
        try
        {
            var resp = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "evaluated")
                .Get();

            var posteriores = (resp.Models ?? new List<TaskModel>())
                .Where(t => t.OutdatedAt == null
                            && (ParseStageNumber(t.Phase) ?? 0) > stageIndex)
                .ToList();

            var marked = 0;
            foreach (var t in posteriores)
            {
                try
                {
                    t.OutdatedAt = DateTime.UtcNow;
                    // Evita PGRST204 com navigation properties no Update.
                    t.Project = null;
                    t.IaEvaluations = null;
                    await _supabase.From<TaskModel>().Update(t);
                    marked++;
                }
                catch (Exception exInner)
                {
                    _logger.LogWarning(exInner,
                        "[Outdated] Falha ao marcar task {TaskId} (phase {Phase}) como desatualizada",
                        t.Id, t.Phase);
                }
            }

            if (marked > 0)
                _logger.LogInformation(
                    "[Outdated] {Count} etapa(s) posteriores a {StageIndex} marcadas como desatualizadas no project {ProjectId}",
                    marked, stageIndex, projectId);

            return marked;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Outdated] Falha ao marcar etapas posteriores a {StageIndex} no project {ProjectId}",
                stageIndex, projectId);
            return 0;
        }
    }

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
