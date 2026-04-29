using IdeorAI.Client;
using System.Collections.Concurrent;

namespace IdeorAI.Services;

public sealed class LlmFallbackService(
    IEnumerable<ILlmClient> clients,
    BackendMetrics metrics,
    ILogger<LlmFallbackService> logger) : ILlmFallbackService
{
    private readonly IReadOnlyList<ILlmClient> _clients =
        clients.OrderBy(c => c.Priority).ToList().AsReadOnly();

    private readonly ConcurrentDictionary<string, ProviderState> _states = new();

    public async Task<LlmResult> GenerateAsync(string prompt, LlmOptions? options = null, CancellationToken ct = default)
    {
        if (_clients.Count == 0)
            throw new LlmUnavailableException("Nenhum provider LLM configurado.", []);

        var errors = new List<Exception>();

        foreach (var client in _clients)
        {
            var state = _states.GetOrAdd(client.ProviderName, _ => new ProviderState(client.Priority));
            try
            {
                logger.LogInformation("[LLM] Tentando provider {Provider} (priority={Priority})",
                    client.ProviderName, client.Priority);

                var result = await client.GenerateAsync(prompt, options, ct);

                state.RecordSuccess();
                metrics.LlmRequestsTotal.Add(1,
                    new("provider", result.ProviderName),
                    new("model", result.ModelName),
                    new("status", "success"));
                metrics.LlmDurationSeconds.Record(result.DurationMs / 1000.0,
                    new KeyValuePair<string, object?>[] { new("provider", result.ProviderName) });

                logger.LogInformation("[LLM] ✅ Sucesso via {Provider}/{Model} — {In}t in, {Out}t out, {Ms}ms",
                    result.ProviderName, result.ModelName, result.InputTokens, result.OutputTokens, result.DurationMs);

                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento explícito do caller — propagar imediatamente
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // Timeout interno do Polly — tratar como falha e tentar próximo provider
                state.RecordFailure();
                metrics.LlmRequestsTotal.Add(1,
                    new("provider", client.ProviderName),
                    new("model", "unknown"),
                    new("status", "timeout"));
                logger.LogWarning(ex, "[LLM] ⏱️ Timeout no provider {Provider}, tentando próximo",
                    client.ProviderName);
                errors.Add(ex);
            }
            catch (Exception ex)
            {
                state.RecordFailure();
                metrics.LlmRequestsTotal.Add(1,
                    new("provider", client.ProviderName),
                    new("model", "unknown"),
                    new("status", "error"));

                logger.LogWarning(ex, "[LLM] ❌ Falha no provider {Provider}: {Message}",
                    client.ProviderName, ex.Message);
                errors.Add(ex);
            }
        }

        var summary = string.Join("; ", errors.Select((e, i) => $"{_clients[i].ProviderName}: {e.Message}"));
        throw new LlmUnavailableException(
            $"Todos os {_clients.Count} provider(s) LLM falharam. Detalhes: {summary}",
            errors.AsReadOnly());
    }

    public IReadOnlyDictionary<string, LlmProviderHealth> GetProviderHealth()
    {
        var result = new Dictionary<string, LlmProviderHealth>(_clients.Count);
        foreach (var client in _clients)
        {
            var state = _states.GetOrAdd(client.ProviderName, _ => new ProviderState(client.Priority));
            result[client.ProviderName] = new LlmProviderHealth(
                client.ProviderName,
                client.Priority,
                state.ConsecutiveFailures,
                state.LastSuccessAt,
                state.LastFailureAt);
        }
        return result;
    }

    private sealed class ProviderState(int priority)
    {
        private int _consecutiveFailures;
        public int Priority { get; } = priority;
        public DateTimeOffset? LastSuccessAt { get; private set; }
        public DateTimeOffset? LastFailureAt { get; private set; }
        public int ConsecutiveFailures => _consecutiveFailures;

        public void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            LastSuccessAt = DateTimeOffset.UtcNow;
        }

        public void RecordFailure()
        {
            Interlocked.Increment(ref _consecutiveFailures);
            LastFailureAt = DateTimeOffset.UtcNow;
        }
    }
}
