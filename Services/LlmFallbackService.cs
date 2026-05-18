using IdeorAI.Client;
using IdeorAI.Model.SupabaseModels;
using System.Collections.Concurrent;

namespace IdeorAI.Services;

public sealed class LlmFallbackService(
    IEnumerable<ILlmClient> clients,
    BackendMetrics metrics,
    Supabase.Client supabase,
    ILogger<LlmFallbackService> logger) : ILlmFallbackService
{
    // TODO: re-habilitar fallback Gemini/OpenRouter quando precisarmos.
    // Por enquanto, usamos APENAS DeepSeek como provider único.
    // Se DeepSeek falhar, retornamos LlmUnavailableException (sem cascata).
    private readonly IReadOnlyList<ILlmClient> _clients =
        clients
            .Where(c => string.Equals(c.ProviderName, "DeepSeek", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Priority)
            .ToList()
            .AsReadOnly();

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

                if (options?.SkipCentralMetrics != true)
                    _ = RecordTokenUsageAsync(result, options, prompt, ct);

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

    private async Task RecordTokenUsageAsync(LlmResult result, LlmOptions? options, string prompt, CancellationToken ct)
    {
        try
        {
            var totalTokens = result.InputTokens + result.OutputTokens > 0
                ? result.InputTokens + result.OutputTokens
                : prompt.Length / 4;

            var sourceTag = options?.SourceContext ?? result.ProviderName;

            var record = new IaEvaluationModel
            {
                Id = Guid.NewGuid().ToString(),
                TaskId = null, // chamada não associada a uma task — usamos source_context
                UserId = options?.UserId,
                SourceContext = sourceTag,
                InputText = prompt.Length > 500 ? prompt[..500] + "…" : prompt,
                OutputJson = null,
                ModelUsed = $"{result.ProviderName}/{result.ModelName}",
                TokensUsed = totalTokens,
                InputTokens = result.InputTokens > 0 ? result.InputTokens : null,
                OutputTokens = result.OutputTokens > 0 ? result.OutputTokens : null,
                CreatedAt = DateTime.UtcNow,
            };

            logger.LogInformation("[LLM-Monitor] Tentando registrar tokens — provider={Provider}, source={Source}, total={Tot}t",
                result.ProviderName, sourceTag, totalTokens);

            await supabase.From<IaEvaluationModel>().Insert(record, cancellationToken: ct);

            logger.LogInformation("[LLM-Monitor] ✅ Tokens registrados (id={Id}, source={Source}, model={Model}, tokens={Tot})",
                record.Id, sourceTag, record.ModelUsed, totalTokens);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LLM-Monitor] ❌ Falha ao registrar tokens — provider={Provider}, ex={ExType}: {Msg}",
                result.ProviderName, ex.GetType().Name, ex.Message);
        }
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
