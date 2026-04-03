using IdeorAI.Model.DTOs;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

/// <summary>
/// Controller para gerenciamento de resumos de etapas (contexto acumulado)
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/stage-summaries")]
public class StageSummariesController : ControllerBase
{
    private readonly IStageSummaryService _stageSummaryService;
    private readonly IProjectService _projectService;
    private readonly ILogger<StageSummariesController> _logger;

    public StageSummariesController(
        IStageSummaryService stageSummaryService,
        IProjectService projectService,
        ILogger<StageSummariesController> logger)
    {
        _stageSummaryService = stageSummaryService;
        _projectService = projectService;
        _logger = logger;
    }

    /// <summary>
    /// Busca todos os resumos salvos de um projeto
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<StageSummariesResponseDto>> GetStageSummaries(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("[StageSummaries] Buscando resumos para projeto {ProjectId}, user {UserId}", 
            projectId, userId);

        // Validar input
        if (projectId == Guid.Empty)
        {
            return BadRequest(new { error = "ProjectId inválido" });
        }

        if (userId == Guid.Empty)
        {
            return Unauthorized(new { error = "UserId não fornecido" });
        }

        // Verificar se usuário tem acesso ao projeto
        var hasAccess = await _projectService.UserHasAccessToProjectAsync(userId, projectId);
        if (!hasAccess)
        {
            _logger.LogWarning("[StageSummaries] Acesso negado: user {UserId} ao projeto {ProjectId}", 
                userId, projectId);
            return NotFound(new { error = "Projeto não encontrado ou acesso negado" });
        }

        try
        {
            var summaries = await _stageSummaryService.GetByProjectAsync(projectId);

            var response = new StageSummariesResponseDto
            {
                ProjectId = projectId,
                Summaries = summaries.Select(s => new StageSummaryDto
                {
                    Stage = s.Stage,
                    StageNumber = ExtractStageNumber(s.Stage),
                    StageName = GetStageName(s.Stage),
                    Summary = s.SummaryText,
                    GeneratedAt = s.CreatedAt
                }).ToList()
            };

            _logger.LogInformation("[StageSummaries] Encontrados {Count} resumos para projeto {ProjectId}", 
                response.Summaries.Count, projectId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StageSummaries] Erro ao buscar resumos para projeto {ProjectId}", projectId);
            return StatusCode(500, new { error = "Erro interno ao buscar resumos" });
        }
    }

    /// <summary>
    /// Busca o status de todas as etapas (para badges de invalidação)
    /// </summary>
    [HttpGet("statuses")]
    public async Task<ActionResult<StageStatusesResponseDto>> GetStageStatuses(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        _logger.LogInformation("[StageSummaries] Buscando status das etapas para projeto {ProjectId}, user {UserId}", 
            projectId, userId);

        // Validar input
        if (projectId == Guid.Empty)
        {
            return BadRequest(new { error = "ProjectId inválido" });
        }

        if (userId == Guid.Empty)
        {
            return Unauthorized(new { error = "UserId não fornecido" });
        }

        // Verificar acesso
        var hasAccess = await _projectService.UserHasAccessToProjectAsync(userId, projectId);
        if (!hasAccess)
        {
            return NotFound(new { error = "Projeto não encontrado ou acesso negado" });
        }

        try
        {
            var summaries = await _stageSummaryService.GetByProjectAsync(projectId);
            var summariesDict = summaries.ToDictionary(s => s.Stage?.ToLower() ?? "", s => s);

            // Verificar todas as 5 etapas do MVP
            var allStages = new[] { "etapa1", "etapa2", "etapa3", "etapa4", "etapa5" };
            var stageStatuses = new List<StageStatusDto>();

            for (int i = 0; i < allStages.Length; i++)
            {
                var stage = allStages[i];
                var summary = summariesDict.ContainsKey(stage) ? summariesDict[stage] : null;
                var hasSummary = summary != null;
                
                // Verificar se alguma etapa anterior foi atualizada APÓS esta etapa
                var isInvalidated = false;
                if (hasSummary && i > 0)
                {
                    var previousStages = allStages.Take(i);
                    foreach (var prevStage in previousStages)
                    {
                        if (summariesDict.ContainsKey(prevStage))
                        {
                            var prevSummary = summariesDict[prevStage];
                            // Se a etapa anterior foi atualizada depois desta, esta está inválida
                            if (prevSummary != null && summary != null && prevSummary.UpdatedAt > summary.UpdatedAt)
                            {
                                isInvalidated = true;
                                break;
                            }
                        }
                    }
                }

                string status;
                string message;

                if (!hasSummary)
                {
                    status = "pending";
                    message = "Contexto pendente";
                }
                else if (isInvalidated)
                {
                    status = "invalidated";
                    message = "Precisa ser regerado (etapa anterior modificada)";
                }
                else
                {
                    status = "valid";
                    message = "Contexto salvo";
                }

                stageStatuses.Add(new StageStatusDto
                {
                    StageNumber = i + 1,
                    Stage = stage,
                    HasSummary = hasSummary,
                    IsValid = status == "valid",
                    Status = status,
                    Message = message
                });
            }

            var response = new StageStatusesResponseDto
            {
                ProjectId = projectId,
                Stages = stageStatuses
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StageSummaries] Erro ao buscar status das etapas para projeto {ProjectId}", projectId);
            return StatusCode(500, new { error = "Erro interno ao buscar status" });
        }
    }

    /// <summary>
    /// Extrai número da etapa do string (etapa1 -> 1)
    /// </summary>
    private static int ExtractStageNumber(string stage)
    {
        if (string.IsNullOrEmpty(stage))
            return 0;

        var numberPart = stage.Replace("etapa", "");
        if (int.TryParse(numberPart, out var number))
            return number;

        return 0;
    }

    /// <summary>
    /// Retorna nome amigável da etapa
    /// </summary>
    private static string GetStageName(string stage)
    {
        return stage?.ToLower() switch
        {
            "etapa1" => "Ideia e Problema",
            "etapa2" => "Pesquisa de Mercado",
            "etapa3" => "Proposta de Valor",
            "etapa4" => "Modelo de Negócio",
            "etapa5" => "Definição do MVP",
            _ => stage ?? "Etapa"
        };
    }
}

/// <summary>
/// DTO de resposta com lista de resumos
/// </summary>
public class StageSummariesResponseDto
{
    public Guid ProjectId { get; set; }
    public List<StageSummaryDto> Summaries { get; set; } = new();
}

/// <summary>
/// DTO de um resumo individual
/// </summary>
public class StageSummaryDto
{
    public string Stage { get; set; } = null!;
    public int StageNumber { get; set; }
    public string StageName { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// DTO de resposta com status das etapas
/// </summary>
public class StageStatusesResponseDto
{
    public Guid ProjectId { get; set; }
    public List<StageStatusDto> Stages { get; set; } = new();
}

/// <summary>
/// DTO de status de uma etapa
/// </summary>
public class StageStatusDto
{
    public int StageNumber { get; set; }
    public string Stage { get; set; } = null!;
    public bool HasSummary { get; set; }
    public bool IsValid { get; set; }
    public string Status { get; set; } = null!; // "valid" | "pending" | "invalidated"
    public string Message { get; set; } = null!;
}
