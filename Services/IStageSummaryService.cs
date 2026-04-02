using IdeorAI.Model.SupabaseModels;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Interface para serviço de gerenciamento de resumos de etapas
/// </summary>
public interface IStageSummaryService
{
    /// <summary>
    /// Cria ou atualiza o resumo de uma etapa (UPSERT)
    /// </summary>
    Task<ProjectStageSummaryModel?> UpsertAsync(Guid projectId, Guid userId, string stage, JsonElement summaryJson, string summaryText);

    /// <summary>
    /// Busca todos os resumos de um projeto
    /// </summary>
    Task<List<ProjectStageSummaryModel>> GetByProjectAsync(Guid projectId);

    /// <summary>
    /// Busca resumos das etapas anteriores para contexto acumulado
    /// </summary>
    Task<List<ProjectStageSummaryModel>> GetPreviousStagesAsync(Guid projectId, string currentStage);

    /// <summary>
    /// Deleta resumos das etapas posteriores (invalidação ao regenerar)
    /// </summary>
    Task DeleteSubsequentStagesAsync(Guid projectId, string stage);
}
