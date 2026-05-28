using IdeorAI.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace IdeorAI.Middleware;

/// <summary>
/// Valida JWT do Supabase Auth e injeta x-user-id nos controllers.
/// Suporte a HS256 (JWT secret) e RS256 (JWKS).
/// Onda 2: issuer/audience validation, JWKS lock, PII scrub em logs.
/// </summary>
public class JwtAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtAuthMiddleware> _logger;
    private readonly string _supabaseUrl;
    private readonly string _jwtSecret;
    private readonly string _validIssuer;
    private readonly string _validAudience;
    private readonly bool _requireAuth;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;

    // Semáforo para evitar thundering-herd no fetch do JWKS (#19)
    private static readonly SemaphoreSlim _jwksLock = new(1, 1);

    private static readonly string[] PublicRoutes =
    [
        "/api/health",
        "/swagger",
        "/api/leads",
    ];

    public JwtAuthMiddleware(
        RequestDelegate next,
        ILogger<JwtAuthMiddleware> logger,
        IConfiguration configuration,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory)
    {
        _next = next;
        _logger = logger;
        _supabaseUrl = configuration["Supabase:Url"] ?? "";
        _jwtSecret   = configuration["Supabase:JwtSecret"] ?? "";
        _requireAuth = configuration.GetValue<bool>("Auth:RequireJwt", false);
        _cache = cache;
        _httpClientFactory = httpClientFactory;

        // Issuer padrão Supabase: {url}/auth/v1  (#1)
        _validIssuer   = configuration["Auth:ValidIssuer"]
                         ?? (!string.IsNullOrWhiteSpace(_supabaseUrl)
                             ? $"{_supabaseUrl.TrimEnd('/')}/auth/v1"
                             : "");
        // Audience padrão Supabase: "authenticated"  (#1)
        _validAudience = configuration["Auth:ValidAudience"] ?? "authenticated";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (IsPublicRoute(path))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            var userId = await ValidateSupabaseJwtAsync(token);

            if (userId == null)
            {
                if (string.IsNullOrWhiteSpace(_jwtSecret) && string.IsNullOrWhiteSpace(_supabaseUrl))
                {
                    _logger.LogDebug("Bearer sem config JWT — modo legado ativo para {Path}", path);
                }
                else
                {
                    _logger.LogWarning("JWT inválido ou expirado para {Path}", path);
                    AddCorsHeaders(context);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Token inválido ou expirado\"}");
                    return;
                }
            }
            else
            {
                context.Request.Headers["x-user-id"] = userId;
                // #20 — loga hash do userId, não o UUID real
                _logger.LogDebug("JWT validado. User={UserHash}", PiiScrubber.HashUserId(userId));
            }
        }
        else if (_requireAuth)
        {
            _logger.LogWarning("Request sem Authorization header para {Path} — rejeitado (RequireJwt=true)", path);
            AddCorsHeaders(context);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Authorization header obrigatório\"}");
            return;
        }

        await _next(context);
    }

    private async Task<string?> ValidateSupabaseJwtAsync(string token)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_jwtSecret))
                return ValidateWithSecret(token);

            if (!string.IsNullOrWhiteSpace(_supabaseUrl))
                return await ValidateWithJwks(token);

            _logger.LogWarning("Nenhuma configuração de validação JWT (Supabase:JwtSecret ou Supabase:Url)");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao validar JWT");
            return null;
        }
    }

    private string? ValidateWithSecret(string token)
    {
        var secretBytes = Encoding.UTF8.GetBytes(_jwtSecret);
        var handler = new JsonWebTokenHandler();

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secretBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            // #1 — issuer/audience validation
            ValidateIssuer   = !string.IsNullOrWhiteSpace(_validIssuer),
            ValidIssuer      = _validIssuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(_validAudience),
            ValidAudience    = _validAudience,
        };

        var result = handler.ValidateToken(token, parameters);
        if (!result.IsValid) return null;

        result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub);
        return sub?.ToString();
    }

    private async Task<string?> ValidateWithJwks(string token)
    {
        const string cacheKey = "supabase_jwks";
        var jwksUrl = $"{_supabaseUrl.TrimEnd('/')}/auth/v1/.well-known/jwks.json";

        // #19 — JWKS lock: apenas 1 fetch simultâneo, os demais aguardam o cache
        if (!_cache.TryGetValue(cacheKey, out JsonWebKeySet? jwks) || jwks == null)
        {
            await _jwksLock.WaitAsync();
            try
            {
                // Double-check após adquirir o lock
                if (!_cache.TryGetValue(cacheKey, out jwks) || jwks == null)
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    var jwksJson = await httpClient.GetStringAsync(jwksUrl);
                    jwks = new JsonWebKeySet(jwksJson);
                    _cache.Set(cacheKey, jwks, TimeSpan.FromMinutes(10));
                    _logger.LogDebug("JWKS carregado do Supabase e cacheado (10 min)");
                }
            }
            finally
            {
                _jwksLock.Release();
            }
        }

        var handler = new JsonWebTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks!.GetSigningKeys(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            // #1 — issuer/audience validation
            ValidateIssuer   = !string.IsNullOrWhiteSpace(_validIssuer),
            ValidIssuer      = _validIssuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(_validAudience),
            ValidAudience    = _validAudience,
        };

        var result = handler.ValidateToken(token, parameters);
        if (!result.IsValid) return null;

        result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub);
        return sub?.ToString();
    }

    private static bool IsPublicRoute(string path) =>
        PublicRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));

    private static void AddCorsHeaders(HttpContext context)
    {
        var origin = context.Request.Headers["Origin"].ToString();
        if (string.IsNullOrEmpty(origin) || !IsOriginAllowed(origin)) return;
        context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
    }

    private static bool IsOriginAllowed(string origin)
    {
        if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.Equals("https://ideorai.com", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.Equals("https://www.ideorai.com", StringComparison.OrdinalIgnoreCase)) return true;
        // Aceita apenas previews do projeto específico — não qualquer *.vercel.app
        if (origin.StartsWith("https://frontend-ideor-ais-projects", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
