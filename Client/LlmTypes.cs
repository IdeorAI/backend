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

public record LlmProviderHealth(
    string ProviderName,
    int Priority,
    int ConsecutiveFailures,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt)
{
    public bool IsHealthy   => ConsecutiveFailures == 0;
    public bool IsDegraded  => ConsecutiveFailures is > 0 and < 5;
    public bool IsUnhealthy => ConsecutiveFailures >= 5;
}

public sealed class LlmUnavailableException(
    string message,
    IReadOnlyList<Exception> innerExceptions)
    : Exception(message)
{
    public IReadOnlyList<Exception> InnerExceptions { get; } = innerExceptions;
}
