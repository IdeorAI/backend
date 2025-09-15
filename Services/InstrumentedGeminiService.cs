using System.Diagnostics;
using IdeorAI.Client;
using Microsoft.Extensions.Logging;

namespace IdeorAI.Services
{
    public class InstrumentedGeminiService
    {
        private readonly GeminiApiClient _geminiClient;
        private static readonly ActivitySource GeminiActivitySource = new("Gemini");
        private readonly ILogger<InstrumentedGeminiService> _logger;

        public InstrumentedGeminiService(GeminiApiClient geminiClient, ILogger<InstrumentedGeminiService> logger)
        {
            _geminiClient = geminiClient;
            _logger = logger;
        }

        public async Task<string> GenerateContentAsync(string prompt)
        {
            using var activity = GeminiActivitySource.StartActivity("Gemini.GenerateContent");
            activity?.SetTag("ai.model", "gemini-pro");
            activity?.SetTag("ai.prompt_length", prompt.Length);
            
            try
            {
                var response = await _geminiClient.GenerateContentAsync(prompt);
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("ai.response_length", response.Length);
                activity?.SetTag("ai.response_time_ms", activity.Duration.TotalMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("error.message", ex.Message);
                _logger.LogError(ex, "Gemini API call failed");
                throw;
            }
        }
    }
}