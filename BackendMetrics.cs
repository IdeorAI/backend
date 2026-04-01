// BackendMetrics.cs
using System.Diagnostics.Metrics;

namespace IdeorAI.Client
{
    public class BackendMetrics
    {
        private readonly Meter _meter;
        
        public Histogram<double> HttpServerDuration { get; }
        public Histogram<double> GeminiDuration { get; }
        public Counter<long> GeminiErrors { get; }
        public Counter<long> BackendErrors { get; }
        public UpDownCounter<long> RequestsInFlight { get; }

        public BackendMetrics()
        {
            _meter = new Meter("IdeorAI.Backend");
            
            HttpServerDuration = _meter.CreateHistogram<double>(
                "http_server_duration_seconds",
                "seconds",
                "Duration of HTTP server requests");
                
            GeminiDuration = _meter.CreateHistogram<double>(
                "external_gemini_duration_seconds",
                "seconds",
                "Duration of external Gemini API calls");
                
            GeminiErrors = _meter.CreateCounter<long>(
                "external_gemini_errors_total",
                "errors",
                "Total number of Gemini API errors");
                
            BackendErrors = _meter.CreateCounter<long>(
                "backend_errors_total",
                "errors",
                "Total number of backend 5xx errors");
                
            RequestsInFlight = _meter.CreateUpDownCounter<long>(
                "requests_inflight",
                "requests",
                "Number of requests currently in flight");
        }
    }
}