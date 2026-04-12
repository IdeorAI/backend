namespace IdeorAI.Services;

/// <summary>
/// Interface para cálculo do score dinâmico de projetos
/// </summary>
public interface IScoreService
{
    /// <summary>
    /// Calcula o score de um projeto com base nas tasks avaliadas (sem persistir)
    /// </summary>
    Task<decimal> CalculateScoreAsync(string projectId);

    /// <summary>
    /// Calcula e persiste o score no banco de dados
    /// </summary>
    Task<decimal> CalculateAndPersistAsync(string projectId);
}
