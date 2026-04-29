using IdeorAI.Model.SupabaseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

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
    private readonly IMemoryCache _cache;

    public AdminController(Supabase.Client supabase, ILogger<AdminController> logger, IMemoryCache cache)
    {
        _supabase = supabase;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Verifica se o usuário atual é admin, com cache de 60 segundos por userId.
    /// </summary>
    private async Task<bool> IsAdminAsync(Guid userId)
    {
        var cacheKey = $"is_admin_{userId}";
        if (_cache.TryGetValue(cacheKey, out bool cachedResult))
            return cachedResult;

        var result = await _supabase
            .From<ProfileModel>()
            .Select("is_admin")
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Single();

        var isAdmin = result?.IsAdmin ?? false;
        _cache.Set(cacheKey, isAdmin, TimeSpan.FromSeconds(60));
        return isAdmin;
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

        // Buscar TODAS as avaliações no período (todos os usuários)
        // Usando service role key — RLS bypassed completamente
        var evaluations = await _supabase
            .From<IaEvaluationModel>()
            .Select("id, user_id, model_used, tokens_used, input_tokens, output_tokens, created_at, task_id")
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
                TotalInputTokens = 0,
                TotalOutputTokens = 0,
                AvgTokensPerCall = 0,
                ByDay = [],
                ByModel = [],
                ByUser = [],
                RecentCalls = []
            });
        }

        var totalTokens = rows.Sum(r => r.TokensUsed ?? 0);
        var totalInputTokens  = rows.Sum(r => r.InputTokens ?? 0);
        var totalOutputTokens = rows.Sum(r => r.OutputTokens ?? 0);
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

        // Agrupamento por usuário
        var byUser = rows
            .GroupBy(r => r.UserId ?? "unknown")
            .Select(g => new UserStats
            {
                UserId = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(r => r.TokensUsed ?? 0)
            })
            .OrderByDescending(u => u.Tokens)
            .ToList();

        // 20 chamadas mais recentes
        var recentCalls = rows
            .Take(20)
            .Select(r => new RecentCall
            {
                Id = r.Id,
                UserId = r.UserId ?? "unknown",
                TaskId = r.TaskId,
                Model = r.ModelUsed ?? "unknown",
                TokensUsed   = r.TokensUsed ?? 0,
                InputTokens  = r.InputTokens ?? 0,
                OutputTokens = r.OutputTokens ?? 0,
                CreatedAt = r.CreatedAt
            })
            .ToList();

        return Ok(new TokenStatsResponse
        {
            TotalCalls = totalCalls,
            TotalTokens = totalTokens,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            AvgTokensPerCall = totalCalls > 0 ? totalTokens / totalCalls : 0,
            ByDay = byDay,
            ByModel = byModel,
            ByUser = byUser,
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

    /// <summary>
    /// Diagnóstico da feature de token observability — sem restrição de admin
    /// GET /api/admin/diag?userId=xxx
    /// </summary>
    [HttpGet("diag")]
    public async Task<IActionResult> GetDiag(
        [FromQuery] string? userId = null,
        [FromHeader(Name = "x-user-id")] string? headerUserId = null)
    {
        var uid = userId ?? headerUserId ?? "(não fornecido)";

        // 1. Checar is_admin do usuário
        bool isAdmin = false;
        string adminCheckError = "";
        try
        {
            if (Guid.TryParse(uid, out var userGuid))
                isAdmin = await IsAdminAsync(userGuid);
            else
                adminCheckError = "userId inválido (não é GUID)";
        }
        catch (Exception ex)
        {
            adminCheckError = ex.Message;
        }

        // 2. Contar registros em ia_evaluations
        int totalEvaluations = 0;
        string evalError = "";
        object? lastRecord = null;
        try
        {
            var all = await _supabase
                .From<IdeorAI.Model.SupabaseModels.IaEvaluationModel>()
                .Select("id, user_id, model_used, tokens_used, input_tokens, output_tokens, created_at")
                .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(5)
                .Get();

            totalEvaluations = all.Models.Count;
            lastRecord = all.Models.Select(r => new
            {
                r.Id,
                r.UserId,
                r.ModelUsed,
                r.TokensUsed,
                r.InputTokens,
                r.OutputTokens,
                r.CreatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            evalError = ex.Message;
        }

        // 3. Checar perfil do usuário
        string profileError = "";
        object? profileInfo = null;
        try
        {
            if (Guid.TryParse(uid, out var ug))
            {
                var profile = await _supabase
                    .From<IdeorAI.Model.SupabaseModels.ProfileModel>()
                    .Select("id, is_admin")
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, ug.ToString())
                    .Single();

                profileInfo = profile == null
                    ? "perfil não encontrado"
                    : new { profile.IsAdmin };
            }
        }
        catch (Exception ex)
        {
            profileError = ex.Message;
        }

        return Ok(new
        {
            userId = uid,
            isAdmin,
            adminCheckError,
            totalEvaluationsReturned = totalEvaluations,
            evalError,
            last5Evaluations = lastRecord,
            profileInfo,
            profileError,
            timestamp = DateTime.UtcNow
        });
    }
}

// ---- DTOs ----

public record TokenStatsResponse
{
    public int TotalCalls { get; init; }
    public int TotalTokens { get; init; }
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
    public int AvgTokensPerCall { get; init; }
    public List<DayStats> ByDay { get; init; } = [];
    public List<ModelStats> ByModel { get; init; } = [];
    public List<UserStats> ByUser { get; init; } = [];
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

public record UserStats
{
    public string UserId { get; init; } = "";
    public int Calls { get; init; }
    public int Tokens { get; init; }
}

public record RecentCall
{
    public string Id { get; init; } = "";
    public string UserId { get; init; } = "";
    public string TaskId { get; init; } = "";
    public string Model { get; init; } = "";
    public int TokensUsed { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public DateTime CreatedAt { get; init; }
}
