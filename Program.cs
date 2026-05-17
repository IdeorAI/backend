using IdeorAI.Client;
using IdeorAI.Services;
using IdeorAI.Api.Services;
using IdeorAI.HealthChecks;
using IdeorAI.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Http.Resilience;
using Serilog;
using Serilog.Formatting.Compact;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Logs;
using Serilog.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Configuração do Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "IdeorAI.Backend")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(new CompactJsonFormatter(),
        path: "logs/log-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// ========== SUPABASE CLIENT ==========
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseServiceKey = builder.Configuration["Supabase:ServiceRoleKey"];

if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseServiceKey))
{
    Log.Fatal("Supabase configuration is missing. Please set 'Supabase:Url' and 'Supabase:ServiceRoleKey' in environment variables or appsettings.json");
    throw new InvalidOperationException("Supabase config missing. Set 'Supabase:Url' and 'Supabase:ServiceRoleKey'.");
}

Log.Information("Supabase Client configured successfully. URL: {Url}", supabaseUrl);

var supabaseOptions = new Supabase.SupabaseOptions
{
    AutoConnectRealtime = false,
    AutoRefreshToken = false // Service role key não expira — refresh causaria reset do auth e ativaria RLS
};

builder.Services.AddSingleton(provider =>
    new Supabase.Client(supabaseUrl, supabaseServiceKey, supabaseOptions));

// Registrar serviços de negócio
builder.Services.AddScoped<IdeorAI.Services.Chat.IChatService, IdeorAI.Services.Chat.ChatService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IStageService, StageService>();
builder.Services.AddScoped<IDocumentGenerationService, DocumentGenerationService>();
builder.Services.AddScoped<IPdfExportService, PdfExportService>();
builder.Services.AddScoped<IStageSummaryService, StageSummaryService>();
builder.Services.AddScoped<IScoreService, ScoreService>();
builder.Services.AddScoped<IIvoService, IvoService>();
builder.Services.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
builder.Services.AddScoped<IGoPivotService, GoPivotService>();

// Configuração do OpenTelemetry
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("IdeorAI.Backend")
    .AddTelemetrySdk()
    .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName) });

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.SetResourceBuilder(resourceBuilder)
            .AddMeter("IdeorAI.Backend")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter(options =>
        {
            options.ScrapeEndpointPath = "/metrics";
            options.ScrapeResponseCacheDurationMilliseconds = 0;
        });
    })
    .WithTracing(tracing =>
    {
        tracing.SetResourceBuilder(resourceBuilder)
            .AddSource("IdeorAI.Service")
            .AddAspNetCoreInstrumentation(options =>
            {
                options.EnrichWithHttpRequest = (activity, httpRequest) =>
                {
                    var requestId = httpRequest.Headers["x-request-id"].FirstOrDefault();
                    activity.SetTag("http.request_id", requestId);
                    activity.SetTag("http.user_agent", httpRequest.Headers.UserAgent);
                };
                options.EnrichWithHttpResponse = (activity, httpResponse) =>
                {
                    var requestId = httpResponse.Headers["x-request-id"].FirstOrDefault();
                    activity.SetTag("http.response_id", requestId);
                };
                options.EnrichWithException = (activity, exception) =>
                {
                    activity.SetTag("error", true);
                    activity.SetTag("error.message", exception.Message);
                    activity.SetTag("error.stack_trace", exception.StackTrace);
                };
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation(options =>
            {
                options.EnrichWithHttpRequestMessage = (activity, httpRequest) =>
                {
                    activity.SetTag("external.system", "gemini");
                };
                options.RecordException = true;
            });

            // ✅ OTLP Exporter - apenas se configurado
            var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri))
            {
                tracing.AddOtlpExporter(otlpOptions =>
                {
                    otlpOptions.Endpoint = otlpUri;
                    otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
            }

            // Console exporter para debugging
            tracing.AddConsoleExporter();
    });

// Configuração do OpenTelemetry para logs
builder.Services.AddLogging(logging =>
{
    logging.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddConsoleExporter();

        // ✅ OTLP para logs - apenas se configurado
        var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri))
        {
            options.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = otlpUri;
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
        }
    });
});

// Configuração das APIs de IA
var geminiApiKey    = builder.Configuration["Gemini:ApiKey"];
var openRouterApiKey = builder.Configuration["OpenRouter:ApiKey"];
var deepSeekApiKey   = builder.Configuration["DeepSeek:ApiKey"];
var openRouterModel  = builder.Configuration["OpenRouter:Model"]  ?? "google/gemma-3-12b-it:free";
var openRouterModel2 = builder.Configuration["OpenRouter:Model2"];
var openRouterModel3 = builder.Configuration["OpenRouter:Model3"];

if (string.IsNullOrEmpty(geminiApiKey) &&
    string.IsNullOrEmpty(openRouterApiKey) &&
    string.IsNullOrEmpty(deepSeekApiKey))
{
    throw new InvalidOperationException(
        "Nenhuma API de IA configurada. Configure 'DeepSeek:ApiKey', 'OpenRouter:ApiKey' ou 'Gemini:ApiKey'.");
}

// Registrar métricas personalizadas
builder.Services.AddSingleton<BackendMetrics>();

// ── LlmResilienceOptions ─────────────────────────────────────────────────────
builder.Services.Configure<IdeorAI.Options.LlmResilienceOptions>(
    builder.Configuration.GetSection(IdeorAI.Options.LlmResilienceOptions.Section));

// ── ChatOptions ──────────────────────────────────────────────────────────────
builder.Services.Configure<IdeorAI.Options.ChatOptions>(
    builder.Configuration.GetSection(IdeorAI.Options.ChatOptions.Section));

// ── DeepSeek Client (priority 1 — primário) ──────────────────────────────────
if (!string.IsNullOrEmpty(deepSeekApiKey))
{
    builder.Services.Configure<IdeorAI.Options.DeepSeekOptions>(opts =>
    {
        opts.ApiKey      = deepSeekApiKey;
        opts.BaseUrl     = builder.Configuration["DeepSeek:BaseUrl"]     ?? "https://api.deepseek.com";
        opts.Model       = builder.Configuration["DeepSeek:Model"]       ?? "deepseek-v4-flash";
        opts.MaxTokens   = int.TryParse(builder.Configuration["DeepSeek:MaxTokens"],   out var mt) ? mt : 8000;
        opts.Temperature = float.TryParse(builder.Configuration["DeepSeek:Temperature"], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var temp) ? temp : 0.7f;
        opts.TimeoutSeconds = int.TryParse(builder.Configuration["DeepSeek:TimeoutSeconds"], out var ts) ? ts : 60;
    });

    builder.Services.AddHttpClient("DeepSeek", client =>
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", deepSeekApiKey);
    })
    .AddStandardResilienceHandler(options =>
    {
        // 3 retries com backoff exponencial + jitter (2s, 4s, 8s ± jitter)
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.Delay = TimeSpan.FromSeconds(2);

        // Circuit breaker — SamplingDuration deve ser >= 2× AttemptTimeout (regra Polly v8)
        // AttemptTimeout=60s → SamplingDuration >= 120s
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 0.6;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

        // Timeout por tentativa e timeout total (inclui todas as retentativas)
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(240);
    });

    builder.Services.AddSingleton<IdeorAI.Client.ILlmClient, IdeorAI.Client.DeepSeekClient>();
    Log.Information("DeepSeek configurado como provider primário (priority=1) com Polly v8 resilience");
}

// ── OpenRouter Client (priority 2 — fallback) ─────────────────────────────────
if (!string.IsNullOrEmpty(openRouterApiKey))
{
    var models = new List<string> { openRouterModel };
    if (!string.IsNullOrWhiteSpace(openRouterModel2)) models.Add(openRouterModel2);
    if (!string.IsNullOrWhiteSpace(openRouterModel3)) models.Add(openRouterModel3);
    Log.Information("OpenRouter configurado com {Count} modelo(s): {Models}", models.Count, string.Join(", ", models));

    builder.Services.AddHttpClient("OpenRouter", client =>
    {
        client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openRouterApiKey);
        client.DefaultRequestHeaders.Add("HTTP-Referer", "https://ideorai.com");
        client.DefaultRequestHeaders.Add("X-Title", "IdeorAI");
    })
    .AddStandardResilienceHandler(options =>
    {
        // Retry conservador — OpenRouterClient já faz rotação de modelos internamente;
        // Polly cobre apenas falhas de rede/5xx por tentativa individual
        options.Retry.MaxRetryAttempts = 1;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.Delay = TimeSpan.FromSeconds(1);

        // Circuit breaker — SamplingDuration deve ser >= 2× AttemptTimeout (regra Polly v8)
        // AttemptTimeout=90s → SamplingDuration >= 180s
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 0.7;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(180);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(90);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(200);
    });

    builder.Services.AddSingleton<IdeorAI.Client.ILlmClient>(provider =>
        new OpenRouterClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            models,
            provider.GetRequiredService<ILogger<OpenRouterClient>>()));
}

// GeminiApiClient (apenas se nem DeepSeek nem OpenRouter estiverem configurados)
if (string.IsNullOrEmpty(deepSeekApiKey) && string.IsNullOrEmpty(openRouterApiKey) && !string.IsNullOrEmpty(geminiApiKey))
{
    builder.Services.AddHttpClient<GeminiApiClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(90);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            UseProxy = true,
        };
        return handler;
    });
}

// ── LlmFallbackService — orquestra todos os ILlmClient registrados ────────────
builder.Services.AddSingleton<IdeorAI.Services.ILlmFallbackService, IdeorAI.Services.LlmFallbackService>();

// HttpClient adicional para PostgREST direto (se necessário)
builder.Services.AddHttpClient("supabase", client =>
{
    var url = builder.Configuration["Supabase:Url"];
    var key = builder.Configuration["Supabase:ServiceRoleKey"];

    client.BaseAddress = new Uri($"{url!.TrimEnd('/')}/rest/v1/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Add("apikey", key);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    client.DefaultRequestHeaders.Add("Prefer", "return=representation");
});

// HttpClient para HubSpot API
builder.Services.AddHttpClient<HubSpotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});


// CORS — allowlist literal (defesa em profundidade)
const string FrontendCors = "FrontendCors";
var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

var defaultOrigins = new[]
{
    "https://www.ideorai.com",
    "https://ideorai.com",
    "http://localhost:3000",
    "http://localhost:3001",
};

var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins.Concat(defaultOrigins).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
    : defaultOrigins;

// Vercel preview: aceita só origens cujo prefixo foi explicitamente cadastrado
var vercelPreviewPrefixes = builder.Configuration
    .GetSection("Cors:VercelPreviewPrefixes")
    .Get<string[]>() ?? Array.Empty<string>();

Log.Information("CORS: Allowlist com {Count} origens fixas + {PrefixCount} prefixos Vercel",
    allowedOrigins.Length, vercelPreviewPrefixes.Length);

builder.Services.AddCors(opt =>
{
    opt.AddPolicy(FrontendCors, p =>
    {
        p.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;

            foreach (var prefix in vercelPreviewPrefixes)
            {
                if (!string.IsNullOrWhiteSpace(prefix) &&
                    origin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            Log.Debug("CORS: Origin '{Origin}' rejeitado (fora da allowlist)", origin);
            return false;
        })
         .AllowAnyMethod()
         .WithHeaders("Content-Type", "Authorization", "x-user-id", "x-request-id")
         .AllowCredentials()
         .WithExposedHeaders("Content-Disposition", "x-request-id")
         .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddCheck<LlmHealthCheck>("llm-providers", tags: ["llm", "ready"]);

// Rate Limiting — 50 gerações IA por hora.
// Particiona por IP+UserId; ataques que rotacionam x-user-id ainda batem no bucket do IP.
builder.Services.AddRateLimiter(options =>
{
    string PartitionKey(HttpContext ctx)
    {
        var userId = ctx.Request.Headers["x-user-id"].ToString();
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return string.IsNullOrEmpty(userId) ? $"ip:{ip}" : $"{ip}|{userId}";
    }

    options.AddPolicy("ai-generation", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromHours(1),
                PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AiGenerationsPerHour", 50),
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Limit "lead-capture" — endpoint público sem captcha
    options.AddPolicy("lead-capture", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(15),
                PermitLimit = 10,
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "3600";
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Limite de gerações IA atingido. Tente novamente em 1 hora." }, ct);
    };
});

var app = builder.Build();

// Middleware de Request ID
app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers["x-request-id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    context.Response.Headers["x-request-id"] = requestId;
    context.Items["RequestId"] = requestId;

    // Adicionar ao contexto de log
    using (LogContext.PushProperty("RequestId", requestId))
    using (LogContext.PushProperty("UserId", context.User?.Identity?.Name ?? "anonymous"))
    using (LogContext.PushProperty("Route", context.Request.Path))
    using (LogContext.PushProperty("Method", context.Request.Method))
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next();
            stopwatch.Stop();

            Log.Information("Request completed - Status: {StatusCode}, Latency: {LatencyMs}ms",
                context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "Request failed - Status: {StatusCode}, Latency: {LatencyMs}ms",
                context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
});



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Global exception handler — garante CORS headers mesmo em 500
// Deve ficar ANTES de UseCors para capturar exceções de qualquer middleware posterior
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[GlobalExceptionHandler] Unhandled exception: {Type} - {Message}", ex.GetType().Name, ex.Message);

        if (!context.Response.HasStarted)
        {
            // Adicionar CORS headers manualmente para que o browser veja o erro real
            var origin = context.Request.Headers["Origin"].ToString();
            if (!string.IsNullOrEmpty(origin))
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            }
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Internal server error"
            });
        }
    }
});

// IMPORTANTE: CORS deve vir ANTES de UseAuthorization e MapControllers
app.UseCors(FrontendCors);

// Rate Limiter
app.UseRateLimiter();

// Proteger /metrics em produção com token secreto
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/metrics"))
        {
            var expected = app.Configuration["Metrics:SecretToken"];
            var provided = context.Request.Headers["X-Metrics-Token"].ToString();
            if (string.IsNullOrEmpty(expected) || provided != expected)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                return;
            }
        }
        await next();
    });
}

// Middleware JWT: valida Bearer token do Supabase e injeta x-user-id
// Quando Auth:RequireJwt=true, rejeita requests sem JWT válido
// Quando false (padrão), aceita x-user-id header diretamente (modo legado)
app.UseMiddleware<JwtAuthMiddleware>();

// Configurar endpoint Prometheus
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.UseAuthorization();
app.MapControllers();

// Health check simples
app.MapGet("/api/health", () => Results.Ok(new {
    status = "healthy",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName
})).WithTags("Health").AllowAnonymous();

// Health check LLM — retorna estado dos providers com métricas de falhas
app.MapHealthChecks("/api/health/llm", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status    = report.Status.ToString().ToLowerInvariant(),
            duration  = report.TotalDuration,
            providers = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status      = e.Value.Status.ToString().ToLowerInvariant(),
                    description = e.Value.Description,
                    data        = e.Value.Data
                })
        });
    }
}).AllowAnonymous().WithTags("Health");

app.Run();