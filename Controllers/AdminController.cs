using IdeorAI.Model.SupabaseModels;
using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

/// <summary>
/// Controller de administração — acesso restrito a usuários com is_admin = true
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<AdminController> _logger;

    public AdminController(Supabase.Client supabase, ILogger<AdminController> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    /// <summary>
    /// Verifica se o usuário atual é admin
    /// </summary>
    private async Task<bool> IsAdminAsync(Guid userId)
    {
        var result = await _supabase
            .From<ProfileModel>()
            .Select("is_admin")
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Single();

        return result?.IsAdmin ?? false;
    }

    /// <summary>
    /// Retorna estatísticas de uso de tokens da IA
    /// GET /api/admin/token-stats?from=2024-01-01&to=2024-12-31
    /// </summary>
    [HttpGet("token-stats")]
    public async Task<IActionResult> GetTokenStats(
        [FromHeader(Name = "x-user-id")] Guid userId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!await IsAdminAsync(userId))
            return Forbid();

        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = (to ?? DateTime.UtcNow).Date.AddDays(1).AddSeconds(-1);

        _logger.LogInformation("[Admin] Token stats requested by {UserId}, range {From} - {To}",
            userId, fromDate, toDate);

        // Buscar avaliações no período com join em tasks e projects
        var evaluations = await _supabase
            .From<IaEvaluationModel>()
            .Select("id, model_used, tokens_used, created_at, task_id")
            .Filter("created_at", Supabase.Postgrest.Constants.Operator.GreaterThanOrEqual, fromDate.ToString("O"))
            .Filter("created_at", Supabase.Postgrest.Constants.Operator.LessThanOrEqual, toDate.ToString("O"))
            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
            .Get();

        var rows = evaluations.Models;

        if (!rows.Any())
        {
            return Ok(new TokenStatsResponse
            {
                TotalCalls = 0,
                TotalTokens = 0,
                AvgTokensPerCall = 0,
                ByDay = [],
                ByModel = [],
                RecentCalls = []
            });
        }

        var totalTokens = rows.Sum(r => r.TokensUsed ?? 0);
        var totalCalls = rows.Count;

        // Agrupamento por dia
        var byDay = rows
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new DayStats
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Calls = g.Count(),
                Tokens = g.Sum(r => r.TokensUsed ?? 0)
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Agrupamento por modelo
        var byModel = rows
            .GroupBy(r => r.ModelUsed ?? "unknown")
            .Select(g => new ModelStats
            {
                Model = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(r => r.TokensUsed ?? 0)
            })
            .OrderByDescending(m => m.Tokens)
            .ToList();

        // 20 chamadas mais recentes
        var recentCalls = rows
            .Take(20)
            .Select(r => new RecentCall
            {
                Id = r.Id,
                TaskId = r.TaskId,
                Model = r.ModelUsed ?? "unknown",
                TokensUsed = r.TokensUsed ?? 0,
                CreatedAt = r.CreatedAt
            })
            .ToList();

        return Ok(new TokenStatsResponse
        {
            TotalCalls = totalCalls,
            TotalTokens = totalTokens,
            AvgTokensPerCall = totalCalls > 0 ? totalTokens / totalCalls : 0,
            ByDay = byDay,
            ByModel = byModel,
            RecentCalls = recentCalls
        });
    }

    /// <summary>
    /// Verifica se o usuário logado é admin (usado pelo frontend para mostrar/esconder link)
    /// GET /api/admin/me
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetAdminStatus(
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var isAdmin = await IsAdminAsync(userId);
        return Ok(new { isAdmin });
    }
}

// ---- DTOs ----

public record TokenStatsResponse
{
    public int TotalCalls { get; init; }
    public int TotalTokens { get; init; }
    public int AvgTokensPerCall { get; init; }
    public List<DayStats> ByDay { get; init; } = [];
    public List<ModelStats> ByModel { get; init; } = [];
    public List<RecentCall> RecentCalls { get; init; } = [];
}

public record DayStats
{
    public string Date { get; init; } = "";
    public int Calls { get; init; }
    public int Tokens { get; init; }
}

public record ModelStats
{
    public string Model { get; init; } = "";
    public int Calls { get; init; }
    public int Tokens { get; init; }
}

public record RecentCall
{
    public string Id { get; init; } = "";
    public string TaskId { get; init; } = "";
    public string Model { get; init; } = "";
    public int TokensUsed { get; init; }
    public DateTime CreatedAt { get; init; }
}
