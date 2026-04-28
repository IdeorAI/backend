namespace IdeorAI.Client;

public interface ILlmClient
{
    string ProviderName { get; }
    int Priority { get; }
    Task<LlmResult> GenerateAsync(string prompt, LlmOptions? options = null, CancellationToken ct = default);
}
