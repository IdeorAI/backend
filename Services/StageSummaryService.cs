using IdeorAI.Model.SupabaseModels;
using Supabase;
using Supabase.Postgrest;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço para gerenciamento de resumos de etapas (contexto acumulado)
/// </summary>
public class StageSummaryService : IStageSummaryService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<StageSummaryService> _logger;

    // Ordem das etapas para comparação
    private static readonly string[] StageOrder = { "etapa1", "etapa2", "etapa3", "etapa4", "etapa5" };

    public StageSummaryService(Supabase.Client supabase, ILogger<StageSummaryService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    /// <summary>
    /// Cria ou atualiza o resumo de uma etapa (UPSERT)
    /// </summary>
    public async Task<ProjectStageSummaryModel?> UpsertAsync(
        Guid projectId, 
        Guid userId, 
        string stage, 
        JsonElement summaryJson, 
        string summaryText)
    {
        try
        {
            _logger.LogInformation("[StageSummary] UPSERT para project {ProjectId}, stage {Stage}", projectId, stage);

            // Verificar se já existe
            var existing = await _supabase
                .From<ProjectStageSummaryModel>()
                .Where(x => x.ProjectId == projectId.ToString() && x.Stage == stage)
                .Single();

            if (existing != null)
            {
                // Atualizar
                existing.SummaryJson = summaryJson;
                existing.SummaryText = summaryText;
                existing.UpdatedAt = DateTime.UtcNow;

                var response = await _supabase
                    .From<ProjectStageSummaryModel>()
                    .Update(existing);

                _logger.LogInformation("[StageSummary] Resumo atualizado para {Stage}", stage);
                return response.Models?.FirstOrDefault();
            }
            else
            {
                // Criar novo
                var newSummary = new ProjectStageSummaryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = projectId.ToString(),
                    UserId = userId.ToString(),
                    Stage = stage,
                    SummaryJson = summaryJson,
                    SummaryText = summaryText,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var response = await _supabase
                    .From<ProjectStageSummaryModel>()
                    .Insert(newSummary);

                _logger.LogInformation("[StageSummary] Resumo criado para {Stage}", stage);
                return response.Models?.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StageSummary] Erro no UPSERT para {Stage}", stage);
            return null;
        }
    }

    /// <summary>
    /// Busca todos os resumos de um projeto
    /// </summary>
    public async Task<List<ProjectStageSummaryModel>> GetByProjectAsync(Guid projectId)
    {
        try
        {
            var response = await _supabase
                .From<ProjectStageSummaryModel>()
                .Where(x => x.ProjectId == projectId.ToString())
                .Get();

            // Ordenação em memória (LINQ) ao invés de na query
            return response.Models?
                .OrderBy(x => x.Stage)
                .ToList() ?? new List<ProjectStageSummaryModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StageSummary] Erro ao buscar resumos do projeto {ProjectId}", projectId);
            return new List<ProjectStageSummaryModel>();
        }
    }

    /// <summary>
    /// Busca resumos das etapas anteriores para contexto acumulado
    /// </summary>
    public async Task<List<ProjectStageSummaryModel>> GetPreviousStagesAsync(Guid projectId, string currentStage)
    {
        try
        {
            var currentIndex = Array.IndexOf(StageOrder, currentStage?.ToLower());
            if (currentIndex <= 0)
            {
                // Etapa 1 não tem anteriores
                return new List<ProjectStageSummaryModel>();
            }

            // Buscar todas as etapas anteriores
            var previousStages = StageOrder.Take(currentIndex).ToList();

            var response = await _supabase
                .From<ProjectStageSummaryModel>()
                .Where(x => x.ProjectId == projectId.ToString())
                .Get();

            // Filtrar só as anteriores e ordenar
            var result = response.Models?
                .Where(x => previousStages.Contains(x.Stage?.ToLower() ?? ""))
                .OrderBy(x => Array.IndexOf(StageOrder, x.Stage?.ToLower() ?? ""))
                .ToList() ?? new List<ProjectStageSummaryModel>();

            _logger.LogInformation("[StageSummary] Encontradas {Count} etapas anteriores para {CurrentStage}", 
                result.Count, currentStage);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StageSummary] Erro ao buscar etapas anteriores para {Stage}", currentStage);
            return new List<ProjectStageSummaryModel>();
        }
    }

    /// <summary>
    /// Deleta resumos das etapas posteriores (invalidação ao regenerar)
    /// </summary>
    public async Task DeleteSubsequentStagesAsync(Guid projectId, string stage)
    {
        try
        {
            var currentIndex = Array.IndexOf(StageOrder, stage?.ToLower());
            if (currentIndex < 0 || currentIndex >= StageOrder.Length - 1)
            {
                // Última etapa ou inválida, não há posteriores
                _logger.LogInformation("[StageSummary] Não há etapas posteriores para deletar (stage: {Stage})", stage);
                return;
            }

            // Etapas posteriores
            var subsequentStages = StageOrder.Skip(currentIndex + 1).ToList();

            _logger.LogInformation("[StageSummary] Deletando etapas posteriores a {Stage}: {Stages}", 
                stage, string.Join(", ", subsequentStages));

            // B-03: Batch delete ao invés de loop N+1
            if (subsequentStages.Any())
            {
                await _supabase
                    .From<ProjectStageSummaryModel>()
                    .Where(x => x.ProjectId == projectId.ToString() && subsequentStages.Contains(x.Stage))
                    .Delete();
            }

            _logger.LogInformation("[StageSummary] Etapas posteriores deletadas com sucesso");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StageSummary] Erro ao deletar etapas posteriores para {Stage}", stage);
        }
    }
}
