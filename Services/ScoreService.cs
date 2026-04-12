using IdeorAI.Model.SupabaseModels;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de cálculo do score dinâmico de projetos.
/// Fórmula: 15 pts por task com status "evaluated"
///          +3 pts por task cujo content.Length >= 100
///          +10 pts bônus se todas as 5 tasks estão evaluated
///          Máximo: 100 pts
/// </summary>
public class ScoreService : IScoreService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ScoreService> _logger;

    private const int PtsPerEvaluatedTask = 15;
    private const int PtsPerRichContent = 3;
    private const int PtsAllCompleteBonus = 10;
    private const int TotalStages = 5;
    private const int MinContentLength = 100;
    private const int MaxScore = 100;

    public ScoreService(Supabase.Client supabase, ILogger<ScoreService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    public async Task<decimal> CalculateScoreAsync(string projectId)
    {
        try
        {
            var tasks = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Get();

            return ComputeScore(tasks.Models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating score for project {ProjectId}", projectId);
            return 0;
        }
    }

    public async Task<decimal> CalculateAndPersistAsync(string projectId)
    {
        var score = await CalculateScoreAsync(projectId);

        try
        {
            var project = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Single();

            if (project != null)
            {
                project.Score = score;
                await _supabase
                    .From<ProjectModel>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                    .Update(project);

                _logger.LogInformation("Score {Score} persisted for project {ProjectId}", score, projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting score for project {ProjectId}", projectId);
        }

        return score;
    }

    private decimal ComputeScore(IEnumerable<TaskModel> tasks)
    {
        var evaluatedTasks = tasks
            .Where(t => t.Status == "evaluated")
            .ToList();

        int pts = 0;

        // 15 pts por cada task avaliada
        pts += evaluatedTasks.Count * PtsPerEvaluatedTask;

        // +3 pts por task com conteúdo rico (>= 100 chars)
        pts += evaluatedTasks.Count(t => (t.Content?.Length ?? 0) >= MinContentLength) * PtsPerRichContent;

        // +10 pts bônus se todas as 5 tasks estão completas
        if (evaluatedTasks.Count >= TotalStages)
        {
            pts += PtsAllCompleteBonus;
        }

        return Math.Min(pts, MaxScore);
    }
}
