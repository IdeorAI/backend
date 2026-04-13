using IdeorAI.Model.DTOs;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de cálculo do IVO (Ideor Value Opportunity Index).
///
/// O IVO é composto por 7 variáveis (1-10):
///   ScoreIVO - progresso geral (ScoreService rescalado de 0-100 para 1-10)
///   O - Originalidade       (Gemini avalia Etapa 1 e Etapa 3)
///   M - Potencial de Mercado (Gemini avalia Etapa 2)
///   V - Validação da Dor    (Gemini avalia Etapa 1)
///   E - Capacidade Execução  (Gemini avalia Etapa 4 e Etapa 5)
///   T - Timing de Mercado   (Gemini avalia Etapa 2)
///   D - Qualidade Documentação (calculado mecanicamente via tasks)
///
/// Fórmula: IVO = (ScoreIVO^1.3 * O * M * V * E * T * D) / 100000
///          IVO_Index = min(100 * (IVO + 1)^2.2, 10_000_000)
/// </summary>
public interface IIvoService
{
    /// <summary>
    /// Avalia as variáveis IVO relevantes para a etapa via Gemini e atualiza o banco.
    /// Mapeamento: etapa1→O,V | etapa2→M,T | etapa3→O | etapa4→E | etapa5→E
    /// </summary>
    Task EvaluateStageAsync(string projectId, int stageNumber, string stageContent);

    /// <summary>
    /// Recalcula D mecanicamente, recomputa IVO_Index e persiste todas as colunas ivo_* no banco.
    /// </summary>
    Task RecalculateAndPersistAsync(string projectId);

    /// <summary>
    /// Retorna DTO com todos os dados IVO do projeto sem recalcular.
    /// </summary>
    Task<IvoDataDto?> GetIvoDataAsync(string projectId);
}
