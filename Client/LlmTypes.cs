namespace IdeorAI.Client;

public record LlmOptions(
    float Temperature = 0.7f,
    int MaxTokens = 8000);

public record LlmResult(
    string Text,
    int InputTokens,
    int OutputTokens,
    string ModelName,
    string ProviderName,
    long DurationMs);

public sealed class LlmUnavailableException(
    string message,
    IReadOnlyList<Exception> innerExceptions)
    : Exception(message)
{
    public IReadOnlyList<Exception> InnerExceptions { get; } = innerExceptions;
}
