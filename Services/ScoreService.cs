using IdeorAI.Model.SupabaseModels;

namespace IdeorAI.Services;

/// <summary>
/// Score multi-dimensional individualizado (Opções A + B):
///   25% Conclusão  — etapas avaliadas / 5
///   15% Profundidade — tier médio de conteúdo (0-3) por thresholds 100/500/1500 chars
///   40% Qualidade IVO — ponderada por categoria (CategoryIvoWeights), escala 1-10
///   20% Marcos     — marcos únicos atingidos × 4 pts (máx 5 marcos)
/// Máximo: 100 pts
/// </summary>
public class ScoreService : IScoreService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ScoreService> _logger;

    private const int TotalStages = 5;
    private const int MaxMilestones = 5;
    private const decimal PtsPerMilestone = 4m;

    public ScoreService(Supabase.Client supabase, ILogger<ScoreService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    public async Task<decimal> CalculateScoreAsync(string projectId)
    {
        try
        {
            var (tasks, project, milestoneCount) = await FetchAsync(projectId);
            return Compute(tasks, project, milestoneCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating score for project {ProjectId}", projectId);
            return 0;
        }
    }

    public async Task<decimal> CalculateAndPersistAsync(string projectId)
    {
        try
        {
            var (tasks, project, milestoneCount) = await FetchAsync(projectId);
            var score = Compute(tasks, project, milestoneCount);

            if (project != null)
            {
                project.Score = score;
                await _supabase
                    .From<ProjectModel>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                    .Update(project);

                var evaluated = tasks.Where(t => string.Equals(t.Status, "evaluated", StringComparison.OrdinalIgnoreCase)).ToList();
                _logger.LogInformation(
                    "Score {Score} persisted for project {ProjectId} (cat={Cat} completion={C:F1} depth={D:F1} quality={Q:F1} milestones={M:F1})",
                    score, projectId, project.Category ?? "n/a",
                    CompletionPts(evaluated),
                    DepthPts(evaluated),
                    QualityPts(project),
                    MilestonePts(milestoneCount));
            }

            return score;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CalculateAndPersistAsync for project {ProjectId}", projectId);
            return 0;
        }
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private async Task<(List<TaskModel> tasks, ProjectModel? project, int milestoneCount)> FetchAsync(string projectId)
    {
        var tasksTask = _supabase
            .From<TaskModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Get();

        var projectTask = _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Single();

        var milestonesTask = _supabase
            .From<ProjectMilestoneModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Get();

        await Task.WhenAll(tasksTask, projectTask, milestonesTask);

        return (tasksTask.Result.Models, projectTask.Result, milestonesTask.Result.Models.Count);
    }

    private static decimal Compute(List<TaskModel> tasks, ProjectModel? project, int milestoneCount)
    {
        var evaluated = tasks.Where(t => string.Equals(t.Status, "evaluated", StringComparison.OrdinalIgnoreCase)).ToList();
        var total = CompletionPts(evaluated)
                  + DepthPts(evaluated)
                  + QualityPts(project)
                  + MilestonePts(milestoneCount);
        return Math.Min(100m, Math.Round(total, 1));
    }

    // 25% — etapas concluídas
    private static decimal CompletionPts(IList<TaskModel> evaluated) =>
        (Math.Min(evaluated.Count, TotalStages) / (decimal)TotalStages) * 25m;

    // 15% — profundidade média do conteúdo (tier 0-3)
    private static decimal DepthPts(IList<TaskModel> evaluated)
    {
        if (evaluated.Count == 0) return 0m;
        var avgTier = evaluated.Average(t => ContentTier(t.Content?.Length ?? 0));
        return ((decimal)avgTier / 3m) * 15m;
    }

    // 40% — qualidade IVO ponderada pela categoria do projeto (Opção A)
    private static decimal QualityPts(ProjectModel? project)
    {
        if (project == null) return 20m; // neutro: 5/10 × 40
        var weighted = CategoryIvoWeights.WeightedIvo(
            project.Category,
            project.IvoO, project.IvoM, project.IvoV, project.IvoE, project.IvoT);
        return (weighted / 10m) * 40m;
    }

    // 20% — marcos únicos atingidos × 4 pts (Opção B)
    private static decimal MilestonePts(int milestoneCount) =>
        Math.Min(milestoneCount, MaxMilestones) * PtsPerMilestone;

    private static int ContentTier(int length) => length switch
    {
        >= 1500 => 3,
        >= 500  => 2,
        >= 100  => 1,
        _       => 0,
    };
}
