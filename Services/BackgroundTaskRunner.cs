namespace IdeorAI.Services;

/// <summary>
/// Helper para fire-and-forget com scope DI dedicado.
/// Evita captive-dependency leak quando o request termina antes da tarefa.
/// </summary>
public interface IBackgroundTaskRunner
{
    /// <summary>
    /// Executa <paramref name="work"/> em Task.Run com scope DI próprio.
    /// Exceções são capturadas e logadas; nunca propagam.
    /// </summary>
    void Run(Func<IServiceProvider, CancellationToken, Task> work, string operation, CancellationToken ct = default);
}

public sealed class BackgroundTaskRunner : IBackgroundTaskRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundTaskRunner> _logger;

    public BackgroundTaskRunner(IServiceScopeFactory scopeFactory, ILogger<BackgroundTaskRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run(Func<IServiceProvider, CancellationToken, Task> work, string operation, CancellationToken ct = default)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                await work(scope.ServiceProvider, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("[BG:{Op}] cancelado", operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BG:{Op}] falha não tratada: {Msg}", operation, ex.Message);
            }
        }, CancellationToken.None);
    }
}
