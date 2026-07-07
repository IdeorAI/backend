using IdeorAI.Model.DTOs;

namespace IdeorAI.Services;

/// <summary>
/// Serviço do "Resumo Financeiro" (Spec 022 v2). Gera/lê a DRE como artefato próprio
/// (task <c>phase='resumo_financeiro'</c>), derivando a síntese canônica da etapa 4.
/// </summary>
public interface IFinancialSummaryService
{
    /// <summary>
    /// Gera o Resumo Financeiro: extrai a DRE da etapa 4 concluída, calcula a síntese
    /// e persiste tudo numa task <c>resumo_financeiro</c>. Idempotente — se já existir,
    /// recalcula/atualiza. Lança <see cref="InvalidOperationException"/> se a etapa 4
    /// não estiver concluída ou não contiver DRE.
    /// </summary>
    Task<FinancialSummaryDto> GenerateAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Retorna a síntese da task <c>resumo_financeiro</c> existente, ou null se não houver.
    /// </summary>
    Task<FinancialSummaryDto?> GetExistingAsync(Guid projectId);

    /// <summary>
    /// Retorna o JSON bruto da DRE da task <c>resumo_financeiro</c> (string content),
    /// ou null. Usado pela geração de PDF do Plano de Negócios.
    /// </summary>
    Task<string?> GetDreContentAsync(Guid projectId);

    /// <summary>
    /// Spec 024 / 022 v3 — preenche a DRE por IA a partir do contexto do projeto
    /// (DESVINCULADO da etapa 4). USO ÚNICO: marca <c>dre_ai_filled_at</c> no content
    /// e recusa nova chamada se já preenchido. Tolerante a falha da LLM (DRE zerada).
    /// </summary>
    Task<FinancialSummaryDto> AiFillAsync(Guid projectId, Guid userId);
}
