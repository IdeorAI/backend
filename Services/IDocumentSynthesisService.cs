namespace IdeorAI.Services;

/// <summary>
/// Serviço de síntese de documentos finais (spec 019).
/// Gera Pitch Deck, Plano de Negócios e Resumo Executivo a partir do conteúdo de etapas.
/// </summary>
public interface IDocumentSynthesisService
{
    Task<string> GeneratePitchDeckAsync(string projectId, string userId, CancellationToken ct);
    Task<string> GenerateBusinessPlanAsync(string projectId, string userId, CancellationToken ct);
    Task<string> GenerateExecutiveSummaryAsync(string projectId, string userId, CancellationToken ct);
}
