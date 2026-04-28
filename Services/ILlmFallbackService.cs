using IdeorAI.Client;

namespace IdeorAI.Services;

public interface ILlmFallbackService
{
    Task<LlmResult> GenerateAsync(string prompt, LlmOptions? options = null, CancellationToken ct = default);
}
