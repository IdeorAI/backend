using IdeorAI.Client;
using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using IdeorAI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IdeorAI.Controllers;

/// <summary>
/// Spec 024 — salvamento de etapas no MODO MANUAL (Colaborativo).
/// Diferente da geração por IA: o conteúdo vem do usuário (texto livre por subitem).
/// Este endpoint salva a task como 'evaluated' (dispara IVO/Score via StageService)
/// e gera o stage_summary de forma DETERMINÍSTICA (concatenação dos textos, sem LLM).
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/manual-stages")]
public class ManualStagesController : ControllerBase
{
    private readonly IStageService _stageService;
    private readonly IStageSummaryService _stageSummaryService;
    private readonly IProjectService _projectService;
    private readonly ILlmFallbackService _llm;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ManualStagesController> _logger;

    public ManualStagesController(
        IStageService stageService,
        IStageSummaryService stageSummaryService,
        IProjectService projectService,
        ILlmFallbackService llm,
        Supabase.Client supabase,
        ILogger<ManualStagesController> logger)
    {
        _stageService = stageService;
        _stageSummaryService = stageSummaryService;
        _projectService = projectService;
        _llm = llm;
        _supabase = supabase;
        _logger = logger;
    }

    // Retorna (allowed, effectiveUserId). Espelha DocumentsController.RequireEditorAsync.
    private async Task<(bool allowed, Guid effectiveUserId)> RequireEditorAsync(Guid projectId, Guid callerId)
    {
        var project = await _projectService.GetByIdAsync(projectId, callerId);
        if (project == null) return (false, Guid.Empty);
        if (project.OwnerId == callerId) return (true, callerId);

        var memberRes = await _supabase
            .From<ProjectMemberModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, callerId.ToString())
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "accepted")
            .Filter("role", Supabase.Postgrest.Constants.Operator.Equals, "editor")
            .Get();

        if (memberRes.Models.Count > 0) return (true, project.OwnerId);
        return (false, Guid.Empty);
    }

    /// <summary>
    /// Salva uma etapa manual. Dois modos:
    /// - <c>draft=true</c>: salva rascunho parcial (status 'draft'). NÃO dispara
    ///   IVO/Score nem stage_summary, NÃO conta como concluída. Permite subitens vazios.
    /// - <c>draft=false</c> (concluir): exige TODOS os subitens preenchidos; salva
    ///   'evaluated' (dispara IVO/Score via StageService) e gera o stage_summary.
    /// Body: { phase, subitems: { key: texto }, draft? }
    /// </summary>
    [HttpPost("save")]
    public async Task<ActionResult<ManualStageResponseDto>> SaveManualStage(
        Guid projectId,
        [FromBody] ManualStageSaveDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Phase) || dto.Subitems is null)
            return BadRequest(new { error = "Informe a fase e os subitens." });

        // Rascunho aceita parcial; conclusão exige ao menos um subitem com texto.
        if (!dto.Draft && !dto.Subitems.Any(kv => !string.IsNullOrWhiteSpace(kv.Value)))
            return BadRequest(new { error = "Preencha os subitens antes de concluir a etapa." });

        var (allowed, effectiveUserId) = await RequireEditorAsync(projectId, userId);
        if (!allowed)
            return StatusCode(403, new { error = "Você precisa ser editor ou dono do projeto." });

        // content salvo = mesmo formato JSON que a IA geraria: { subitem_key: "texto" }.
        var contentJson = JsonSerializer.Serialize(dto.Subitems);
        var targetStatus = dto.Draft ? "draft" : "evaluated";

        // Salva a task (Create ou Update). O StageService dispara IVO/Score só em 'evaluated'.
        // Pega a MAIS RECENTE da phase (tolerante a eventuais duplicatas históricas).
        var existing = (await _stageService.GetProjectTasksAsync(projectId, effectiveUserId))
            ?.Where(t => string.Equals(t.Phase, dto.Phase, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefault();

        ProjectTask? saved;
        if (existing != null)
        {
            // Auto-save (draft) NÃO rebaixa uma etapa já concluída (evaluated).
            var alreadyEvaluated = string.Equals(existing.Status, "evaluated", StringComparison.OrdinalIgnoreCase);
            saved = await _stageService.UpdateTaskAsync(existing.Id, effectiveUserId, t =>
            {
                t.Content = contentJson;
                t.Status = dto.Draft && alreadyEvaluated ? "evaluated" : targetStatus;
            });
        }
        else
        {
            saved = await _stageService.CreateTaskAsync(projectId, effectiveUserId, new ProjectTask
            {
                Phase = dto.Phase,
                Title = $"Etapa {dto.Phase}",
                Description = $"Conteúdo manual de {dto.Phase}",
                Content = contentJson,
                Status = targetStatus,
            });
        }

        if (saved == null)
            return StatusCode(403, new { error = "Não foi possível salvar a etapa." });

        // Rascunho não gera summary (não é contexto até concluir).
        if (!dto.Draft)
        {
            try
            {
                var summaryText = ManualSummaryBuilder.Build(dto.Phase, dto.Subitems);
                using var jsonDoc = JsonDocument.Parse(contentJson);
                await _stageSummaryService.UpsertAsync(
                    projectId, effectiveUserId, dto.Phase, jsonDoc.RootElement, summaryText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ManualStage] Falha ao gerar summary de {Phase} (não crítico)", dto.Phase);
            }
        }

        return Ok(new ManualStageResponseDto
        {
            TaskId = saved.Id,
            Phase = saved.Phase,
            Status = saved.Status,
        });
    }

    /// <summary>
    /// Gera (sugere) o texto de UM subitem usando o contexto do projeto.
    /// Aditivo: a falha aqui nunca bloqueia o preenchimento manual.
    /// Body: { phase, subitemKey, subitemLabel }
    /// </summary>
    [HttpPost("subitem-assist")]
    public async Task<ActionResult<SubitemAiResponseDto>> SubitemAssist(
        Guid projectId,
        [FromBody] SubitemAssistDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Phase) || string.IsNullOrWhiteSpace(dto.SubitemKey))
            return BadRequest(new { error = "Informe a fase e o subitem." });

        var (allowed, effectiveUserId) = await RequireEditorAsync(projectId, userId);
        if (!allowed) return StatusCode(403, new { error = "Acesso negado." });

        var context = await BuildProjectContextAsync(projectId, effectiveUserId, dto.Phase);
        var label = string.IsNullOrWhiteSpace(dto.SubitemLabel) ? dto.SubitemKey : dto.SubitemLabel;

        var prompt =
            $"Você é um consultor de startups. Escreva o conteúdo do item \"{label}\" " +
            $"da etapa \"{dto.Phase}\" de um projeto, em português, de forma objetiva e prática " +
            $"(2-5 frases, sem markdown, sem títulos). Use o contexto abaixo.\n\n{context}";

        return await RunLlmAsync(prompt, effectiveUserId, $"manual-assist:{projectId}:{dto.Phase}:{dto.SubitemKey}");
    }

    /// <summary>
    /// Revisa/melhora o texto que o usuário já escreveu para UM subitem.
    /// Body: { phase, subitemKey, subitemLabel, currentText }
    /// </summary>
    [HttpPost("subitem-review")]
    public async Task<ActionResult<SubitemAiResponseDto>> SubitemReview(
        Guid projectId,
        [FromBody] SubitemReviewDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Phase) || string.IsNullOrWhiteSpace(dto.SubitemKey))
            return BadRequest(new { error = "Informe a fase e o subitem." });
        if (string.IsNullOrWhiteSpace(dto.CurrentText))
            return BadRequest(new { error = "Não há texto para revisar." });

        var (allowed, effectiveUserId) = await RequireEditorAsync(projectId, userId);
        if (!allowed) return StatusCode(403, new { error = "Acesso negado." });

        var context = await BuildProjectContextAsync(projectId, effectiveUserId, dto.Phase);
        var label = string.IsNullOrWhiteSpace(dto.SubitemLabel) ? dto.SubitemKey : dto.SubitemLabel;

        var prompt =
            $"Você é um consultor de startups. Revise e melhore o texto abaixo do item \"{label}\" " +
            $"(etapa \"{dto.Phase}\"), mantendo a intenção do autor. Corrija clareza, concisão e força do argumento. " +
            $"Responda em português, sem markdown, apenas com o texto revisado.\n\n" +
            $"TEXTO DO USUÁRIO:\n{dto.CurrentText}\n\nCONTEXTO DO PROJETO:\n{context}";

        return await RunLlmAsync(prompt, effectiveUserId, $"manual-review:{projectId}:{dto.Phase}:{dto.SubitemKey}");
    }

    // Monta um bloco de contexto (ideia + resumos das etapas anteriores).
    private async Task<string> BuildProjectContextAsync(Guid projectId, Guid userId, string stage)
    {
        var parts = new List<string>();
        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (!string.IsNullOrWhiteSpace(project?.Description))
            parts.Add($"Ideia: {project!.Description}");
        if (!string.IsNullOrWhiteSpace(project?.Category))
            parts.Add($"Categoria: {project!.Category}");
        // Spec 028 — tags de contexto (FOCO do projeto) também no modo manual,
        // para ancorar o subitem-assist/review nas palavras-chave do usuário.
        var tags = (project?.Keywords ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t)).Take(10).ToList();
        if (tags.Count > 0)
            parts.Add($"Foco do projeto (palavras-chave, NÃO desvie delas): {string.Join(", ", tags)}");

        try
        {
            var previous = await _stageSummaryService.GetPreviousStagesAsync(projectId, stage);
            foreach (var s in previous)
                parts.Add($"[{s.Stage}] {s.SummaryText}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManualStage] Falha ao montar contexto (continuando)");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "Sem contexto adicional disponível.";
    }

    private async Task<ActionResult<SubitemAiResponseDto>> RunLlmAsync(
        string prompt, Guid userId, string sourceContext)
    {
        try
        {
            var result = await _llm.GenerateAsync(prompt, new LlmOptions(
                UserId: userId.ToString(),
                SourceContext: sourceContext));
            var text = (result.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return UnprocessableEntity(new { error = "A IA não retornou um texto válido." });
            return Ok(new SubitemAiResponseDto { Text = text });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ManualStage] Falha na IA por subitem");
            return StatusCode(502, new { error = "Não foi possível obter ajuda da IA agora. Tente novamente." });
        }
    }
}

/// <summary>Body do save manual.</summary>
public class ManualStageSaveDto
{
    public string Phase { get; set; } = null!;
    public Dictionary<string, string> Subitems { get; set; } = new();
    /// <summary>true = rascunho parcial (status 'draft', sem IVO/summary); false = concluir.</summary>
    public bool Draft { get; set; }
}

/// <summary>Resposta do save manual.</summary>
public class ManualStageResponseDto
{
    public Guid TaskId { get; set; }
    public string Phase { get; set; } = null!;
    public string Status { get; set; } = null!;
}

/// <summary>Body de "Gerar com IA" por subitem.</summary>
public class SubitemAssistDto
{
    public string Phase { get; set; } = null!;
    public string SubitemKey { get; set; } = null!;
    public string? SubitemLabel { get; set; }
}

/// <summary>Body de "Revisar com IA" por subitem.</summary>
public class SubitemReviewDto
{
    public string Phase { get; set; } = null!;
    public string SubitemKey { get; set; } = null!;
    public string? SubitemLabel { get; set; }
    public string CurrentText { get; set; } = null!;
}

/// <summary>Resposta de ajuda da IA por subitem.</summary>
public class SubitemAiResponseDto
{
    public string Text { get; set; } = null!;
}

/// <summary>
/// Gera o texto-resumo de uma etapa manual concatenando os subitens.
/// Determinístico, sem LLM. Espelha o papel do SummaryTextGenerator, mas para
/// conteúdo de texto livre (não a estrutura aninhada da IA).
/// </summary>
public static class ManualSummaryBuilder
{
    private const int MaxLength = 800;

    public static string Build(string stage, Dictionary<string, string> subitems)
    {
        var parts = subitems
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Humanize(kv.Key)}: {kv.Value.Trim()}");

        var text = string.Join(". ", parts);
        if (string.IsNullOrWhiteSpace(text))
            return $"Etapa {stage} preenchida manualmente.";

        return text.Length <= MaxLength ? text : text[..(MaxLength - 3)] + "...";
    }

    private static string Humanize(string key)
    {
        var words = key.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w =>
            w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
