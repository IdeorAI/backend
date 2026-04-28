using IdeorAI.Client;

namespace IdeorAI.Services;

public sealed class LlmFallbackService(
    IEnumerable<ILlmClient> clients,
    ILogger<LlmFallbackService> logger) : ILlmFallbackService
{
    private readonly IReadOnlyList<ILlmClient> _clients =
        clients.OrderBy(c => c.Priority).ToList().AsReadOnly();

    public async Task<LlmResult> GenerateAsync(string prompt, LlmOptions? options = null, CancellationToken ct = default)
    {
        if (_clients.Count == 0)
            throw new LlmUnavailableException("Nenhum provider LLM configurado.", []);

        var errors = new List<Exception>();

        foreach (var client in _clients)
        {
            try
            {
                logger.LogInformation("[LLM] Tentando provider {Provider} (priority={Priority})",
                    client.ProviderName, client.Priority);

                var result = await client.GenerateAsync(prompt, options, ct);

                logger.LogInformation("[LLM] ✅ Sucesso via {Provider}/{Model} — {In}t in, {Out}t out, {Ms}ms",
                    result.ProviderName, result.ModelName, result.InputTokens, result.OutputTokens, result.DurationMs);

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
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
}
