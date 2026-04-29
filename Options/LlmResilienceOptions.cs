namespace IdeorAI.Options;

public sealed class LlmResilienceOptions
{
    public const string Section = "LlmResilience";

    public int RetryCount { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 2;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 30;
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 60;
}
