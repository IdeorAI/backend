using IdeorAI.Client;
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
using IdeorAI.Services;

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
            })
            // ✅ Alterado para OTLP (mais moderno e compatível)
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri("http://jaeger:4317");
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            })
            // Manter console exporter para debugging
            .AddConsoleExporter();
    });

// Configuração do OpenTelemetry para logs
builder.Services.AddLogging(logging =>
{
    logging.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddConsoleExporter();
        // ✅ Adicionar OTLP para logs também
        options.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
            otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
        });
    });
});

// Configuração da chave Gemini
var apiKey = builder.Configuration["Gemini:ApiKey"];

if (string.IsNullOrEmpty(apiKey))
{
    throw new InvalidOperationException("Gemini API key is not configured. Please set the 'Gemini:ApiKey' configuration value.");
}

// Registrar métricas personalizadas
builder.Services.AddSingleton<BackendMetrics>();

// Registrar GeminiApiClient com as dependências corretas
builder.Services.AddSingleton(provider =>
    new GeminiApiClient(
        apiKey!,
        provider.GetRequiredService<BackendMetrics>(),
        provider.GetRequiredService<ILogger<GeminiApiClient>>()
    )
);

// Registrar serviços instrumentados
builder.Services.AddSingleton<InstrumentedGeminiService>();

// CORS
const string FrontendCors = "FrontendCors";
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(FrontendCors, p =>
    {
        p.WithOrigins("http://localhost:3000", "https://front-end-plum-nu.vercel.app")
         .AllowAnyMethod()
         .AllowAnyHeader()
         .WithExposedHeaders("Content-Disposition", "x-request-id");
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

// ✅ CORREÇÃO: Remover esta linha duplicada
// app.UseMiddleware<RequestIdMiddleware>(); // Já está implementado acima

// Configurar endpoint Prometheus
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.UseCors(FrontendCors);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger(); // ✅ Adicionar UseSwagger() faltando
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();