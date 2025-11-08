using IdeorAI.Client;
using IdeorAI.Data;
using IdeorAI.Services;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

// ========== EF CORE + SUPABASE POSTGRESQL ==========
var connectionString = builder.Configuration.GetConnectionString("SupabaseConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Database connection string is not configured. Please set 'ConnectionStrings:SupabaseConnection'.");
}

builder.Services.AddDbContext<IdeorDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    });

    // Logging de queries SQL em desenvolvimento
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

// Registrar serviços de negócio
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IStageService, StageService>();
builder.Services.AddScoped<IDocumentGenerationService, DocumentGenerationService>();
builder.Services.AddScoped<IPdfExportService, PdfExportService>();

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

// Configuração da chave Gemini
var apiKey = builder.Configuration["Gemini:ApiKey"];

if (string.IsNullOrEmpty(apiKey))
{
    throw new InvalidOperationException("Gemini API key is not configured. Please set the 'Gemini:ApiKey' configuration value.");
}

//validaçao da Api Key do supabase
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseServiceKey = builder.Configuration["Supabase:ServiceRoleKey"];

if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseServiceKey))
{
    throw new InvalidOperationException("Supabase config missing. Set 'Supabase:Url' and 'Supabase:ServiceRoleKey'.");
}


// Registrar métricas personalizadas
builder.Services.AddSingleton<BackendMetrics>();


// GeminiApiClient via HttpClientFactory (timeout + headers default)
builder.Services.AddHttpClient<GeminiApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90); // Aumentado para 90s (prompts complexos demoram mais)
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new SocketsHttpHandler
    {
        // Configurações de pool de conexões
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),

        // Configurações de DNS
        ConnectTimeout = TimeSpan.FromSeconds(10),

        // Configurações de proxy
        UseProxy = true,

        // Permitir certificados SSL inválidos em desenvolvimento (remover em produção)
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
        }
    };
    return handler;
});

builder.Services.AddHttpClient("supabase", client =>
{
    client.BaseAddress = new Uri($"{supabaseUrl!.TrimEnd('/')}/rest/v1/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    // Autenticação do PostgREST
    client.DefaultRequestHeaders.Add("apikey", supabaseServiceKey);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supabaseServiceKey);
    // Para retornar a linha atualizada (útil para logs/validação)
    client.DefaultRequestHeaders.Add("Prefer", "return=representation");
});


// Registrar serviços instrumentados
builder.Services.AddSingleton<InstrumentedGeminiService>();

// CORS - Configuração ajustada para Vercel + Render
const string FrontendCors = "FrontendCors";
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(FrontendCors, p =>
    {
        p.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin))
                return false;

            // Permitir localhost em desenvolvimento
            if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("https://localhost:", StringComparison.OrdinalIgnoreCase))
                return true;

            // Permitir qualquer domínio *.vercel.app (HTTPS obrigatório)
            if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        })
         .AllowAnyMethod()
         .AllowAnyHeader()
         .AllowCredentials()
         .WithExposedHeaders("Content-Disposition", "x-request-id")
         .SetPreflightMaxAge(TimeSpan.FromMinutes(10)); // Cache preflight por 10 minutos
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// IMPORTANTE: CORS deve vir ANTES de UseAuthorization e MapControllers
app.UseCors(FrontendCors);

// Configurar endpoint Prometheus
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.UseAuthorization();
app.MapControllers();

// Health check endpoint simples
app.MapGet("/api/health", () => Results.Ok(new {
    status = "healthy",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName
})).WithTags("Health");

app.Run();