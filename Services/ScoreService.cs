using IdeorAI.Model.SupabaseModels;

namespace IdeorAI.Services;

/// <summary>
/// Score multi-dimensional (Opção A):
///   30% Conclusão  — etapas avaliadas / 5
///   20% Profundidade — tier médio de conteúdo (0-3) por thresholds 100/500/1500 chars
///   50% Qualidade IVO — média de O, M, V, E, T avaliados pelo DeepSeek
/// Máximo: 100 pts
/// </summary>
public class ScoreService : IScoreService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ScoreService> _logger;

    private const int TotalStages = 5;

    public ScoreService(Supabase.Client supabase, ILogger<ScoreService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    public async Task<decimal> CalculateScoreAsync(string projectId)
    {
        try
        {
            var (tasks, project) = await FetchAsync(projectId);
            return Compute(tasks, project);
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
            var (tasks, project) = await FetchAsync(projectId);
            var score = Compute(tasks, project);

            if (project != null)
            {
                project.Score = score;
                await _supabase
                    .From<ProjectModel>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                    .Update(project);

                _logger.LogInformation(
                    "Score {Score} persisted for project {ProjectId} (completion={C:F1} depth={D:F1} quality={Q:F1})",
                    score, projectId,
                    CompletionPts(tasks.Where(t => t.Status == "evaluated").ToList()),
                    DepthPts(tasks.Where(t => t.Status == "evaluated").ToList()),
                    QualityPts(project));
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

    private async Task<(List<TaskModel> tasks, ProjectModel? project)> FetchAsync(string projectId)
    {
        var tasksTask = _supabase
            .From<TaskModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Get();

        var projectTask = _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Single();

        await Task.WhenAll(tasksTask, projectTask);

        return (tasksTask.Result.Models, projectTask.Result);
    }

    private static decimal Compute(List<TaskModel> tasks, ProjectModel? project)
    {
        var evaluated = tasks.Where(t => t.Status == "evaluated").ToList();
        var total = CompletionPts(evaluated) + DepthPts(evaluated) + QualityPts(project);
        return Math.Min(100m, Math.Round(total, 1));
    }

    // 30% — etapas concluídas
    private static decimal CompletionPts(IList<TaskModel> evaluated) =>
        (Math.Min(evaluated.Count, TotalStages) / (decimal)TotalStages) * 30m;

    // 20% — profundidade média do conteúdo (tier 0-3)
    private static decimal DepthPts(IList<TaskModel> evaluated)
    {
        if (evaluated.Count == 0) return 0m;
        var avgTier = evaluated.Average(t => ContentTier(t.Content?.Length ?? 0));
        return ((decimal)avgTier / 3m) * 20m;
    }

    // 50% — qualidade IVO avaliada pelo DeepSeek (média O/M/V/E/T, escala 1-10)
    private static decimal QualityPts(ProjectModel? project)
    {
        if (project == null) return 25m; // neutro: 5/10 × 50
        var avg = (project.IvoO + project.IvoM + project.IvoV + project.IvoE + project.IvoT) / 5m;
        return (avg / 10m) * 50m;
    }

    private static int ContentTier(int length) => length switch
    {
        >= 1500 => 3,
        >= 500  => 2,
        >= 100  => 1,
        _       => 0,
    };
}
