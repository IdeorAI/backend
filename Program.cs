using IdeorAI.Client;
using IdeorAI.Services;
using IdeorAI.Api.Services;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
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
    AutoRefreshToken = true // Não precisamos de realtime para operações REST
};

builder.Services.AddSingleton(provider =>
    new Supabase.Client(supabaseUrl, supabaseServiceKey, supabaseOptions));

// Registrar serviços de negócio
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IStageService, StageService>();
builder.Services.AddScoped<IDocumentGenerationService, DocumentGenerationService>();
builder.Services.AddScoped<IPdfExportService, PdfExportService>();
builder.Services.AddScoped<IStageSummaryService, StageSummaryService>();

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

// Configuração da chave Gemini (opcional se OpenRouter estiver configurado)
var geminiApiKey = builder.Configuration["Gemini:ApiKey"];
var openRouterApiKey = builder.Configuration["OpenRouter:ApiKey"];
var openRouterModel = builder.Configuration["OpenRouter:Model"] ?? "google/gemma-3-12b-it:free";

if (string.IsNullOrEmpty(geminiApiKey) && string.IsNullOrEmpty(openRouterApiKey))
{
    throw new InvalidOperationException("Nenhuma API de IA configurada. Configure 'Gemini:ApiKey' ou 'OpenRouter:ApiKey'.");
}

// Registrar métricas personalizadas
builder.Services.AddSingleton<BackendMetrics>();

// OpenRouter Client (prioritário se configurado)
if (!string.IsNullOrEmpty(openRouterApiKey))
{
    Log.Information("OpenRouter configured with model {Model}", openRouterModel);
    
    builder.Services.AddHttpClient("OpenRouter", client =>
    {
        client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
        client.Timeout = TimeSpan.FromSeconds(90);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openRouterApiKey);
        client.DefaultRequestHeaders.Add("HTTP-Referer", "https://ideorai.com");
        client.DefaultRequestHeaders.Add("X-Title", "IdeorAI");
    });
    
    builder.Services.AddSingleton(provider => 
        new OpenRouterClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            openRouterModel,
            provider.GetRequiredService<ILogger<OpenRouterClient>>()));
}
else
{
    // GeminiApiClient via HttpClientFactory (fallback)
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
} // Fim do else (Gemini fallback)

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

// Registrar serviços instrumentados (somente se Gemini estiver configurado)
if (!string.IsNullOrEmpty(geminiApiKey))
{
    builder.Services.AddSingleton<InstrumentedGeminiService>();
}

// CORS - Configuração ajustada para Vercel + Render
const string FrontendCors = "FrontendCors";
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(FrontendCors, p =>
    {
        p.SetIsOriginAllowed(origin =>
        {
            Log.Information("CORS: Verificando origin '{Origin}'", origin ?? "NULL");

            if (string.IsNullOrWhiteSpace(origin))
            {
                Log.Warning("CORS: Origin vazio ou nulo - REJEITADO");
                return false;
            }

            // Permitir localhost em desenvolvimento
            if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("https://localhost:", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("CORS: Origin localhost detectado - PERMITIDO");
                return true;
            }

            // Permitir qualquer domínio *.vercel.app (HTTPS obrigatório)
            if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("CORS: Origin Vercel detectado - PERMITIDO");
                return true;
            }

            // Permitir domínio de produção Hostinger (www.ideorai.com e ideorai.com)
            if (origin.Equals("https://www.ideorai.com", StringComparison.OrdinalIgnoreCase) ||
                origin.Equals("https://ideorai.com", StringComparison.OrdinalIgnoreCase) ||
                origin.Equals("http://www.ideorai.com", StringComparison.OrdinalIgnoreCase) ||
                origin.Equals("http://ideorai.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("CORS: Origin IdeorAI (Hostinger) detectado - PERMITIDO");
                return true;
            }

            Log.Warning("CORS: Origin '{Origin}' não corresponde a nenhuma regra - REJEITADO", origin);
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