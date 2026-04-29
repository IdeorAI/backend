using IdeorAI.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IdeorAI.HealthChecks;

public sealed class LlmHealthCheck(ILlmFallbackService fallbackService) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var health = fallbackService.GetProviderHealth();

        if (health.Count == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("Nenhum provider LLM configurado."));

        var data = health.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                status          = kv.Value.IsHealthy ? "healthy" : kv.Value.IsDegraded ? "degraded" : "unhealthy",
                priority        = kv.Value.Priority,
                consecutiveFail = kv.Value.ConsecutiveFailures,
                lastSuccess     = kv.Value.LastSuccessAt,
                lastFailure     = kv.Value.LastFailureAt,
            });

        var unhealthyCount = health.Values.Count(p => p.IsUnhealthy);
        var degradedCount  = health.Values.Count(p => p.IsDegraded);

        if (unhealthyCount == health.Count)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Todos os {health.Count} provider(s) LLM com falhas consecutivas ≥ 5.", data: data));

        if (degradedCount > 0 || unhealthyCount > 0)
            return Task.FromResult(HealthCheckResult.Degraded(
                $"{degradedCount + unhealthyCount}/{health.Count} provider(s) com falhas recentes.", data: data));

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{health.Count} provider(s) OK.", data: data));
    }
}
