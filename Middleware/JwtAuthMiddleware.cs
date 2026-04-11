using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace IdeorAI.Middleware;

/// <summary>
/// Middleware que valida o JWT do Supabase Auth e injeta o userId autenticado
/// como header x-user-id para os controllers existentes.
///
/// Estratégia de migração gradual:
/// 1. Se Authorization: Bearer {token} válido → usa userId do claim "sub"
/// 2. Se token inválido → retorna 401
/// 3. Se não há Authorization header → aceita x-user-id diretamente (modo legado)
///
/// IMPORTANTE: O modo legado (sem JWT) deve ser desabilitado quando o
/// frontend for atualizado para enviar Bearer tokens.
/// </summary>
public class JwtAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtAuthMiddleware> _logger;
    private readonly string _supabaseUrl;
    private readonly string _jwtSecret;
    private readonly bool _requireAuth;

    // Rotas que não precisam de autenticação
    private static readonly string[] PublicRoutes =
    [
        "/api/health",
        "/metrics",
        "/swagger",
        "/api/leads"       // Lead capture é público
    ];

    public JwtAuthMiddleware(
        RequestDelegate next,
        ILogger<JwtAuthMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _supabaseUrl = configuration["Supabase:Url"] ?? "";
        _jwtSecret = configuration["Supabase:JwtSecret"] ?? "";
        _requireAuth = configuration.GetValue<bool>("Auth:RequireJwt", false);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Ignorar rotas públicas
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
                _logger.LogWarning("JWT inválido ou expirado para path {Path}", path);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Token inválido ou expirado\"}");
                return;
            }

            // Sobrescreve x-user-id com o userId validado do JWT
            context.Request.Headers["x-user-id"] = userId;
            _logger.LogDebug("JWT validado. UserId={UserId}", userId);
        }
        else if (_requireAuth)
        {
            // Se Auth:RequireJwt=true, rejeitar requests sem Bearer token
            _logger.LogWarning("Request sem Authorization header para path {Path} — rejeitado (RequireJwt=true)", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Authorization header obrigatório\"}");
            return;
        }
        // else: modo legado — x-user-id header passado diretamente (sem validação JWT)

        await _next(context);
    }

    private async Task<string?> ValidateSupabaseJwtAsync(string token)
    {
        try
        {
            // Supabase pode usar HS256 (com JWT secret) ou RS256 (JWKS)
            if (!string.IsNullOrWhiteSpace(_jwtSecret))
            {
                return ValidateWithSecret(token);
            }

            if (!string.IsNullOrWhiteSpace(_supabaseUrl))
            {
                return await ValidateWithJwks(token);
            }

            _logger.LogWarning("Nenhuma configuração de validação JWT disponível (Supabase:JwtSecret ou Supabase:Url)");
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

        var result = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secretBytes),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        });

        if (!result.IsValid) return null;

        result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub);
        return sub?.ToString();
    }

    private async Task<string?> ValidateWithJwks(string token)
    {
        var jwksUrl = $"{_supabaseUrl.TrimEnd('/')}/auth/v1/.well-known/jwks.json";

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var jwksResponse = await httpClient.GetStringAsync(jwksUrl);
        var jwks = new JsonWebKeySet(jwksResponse);

        var handler = new JsonWebTokenHandler();
        var result = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        });

        if (!result.IsValid) return null;

        result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub);
        return sub?.ToString();
    }

    private static bool IsPublicRoute(string path) =>
        PublicRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
}
