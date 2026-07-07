using System.Text;
using System.Text.Json;
using IdeorAI.Model.DTOs;
using IdeorAI.Model.SupabaseModels;
using static Supabase.Postgrest.Constants;

namespace IdeorAI.Services;

/// <summary>
/// Implementação do Resumo Financeiro (Spec 022 v2).
/// Estratégia (decisão 2026-06-10): a DRE NÃO é regerada por LLM aqui — é
/// EXTRAÍDA da task etapa4 já gerada, garantindo números idênticos aos que o
/// usuário viu, sem custo de LLM. A síntese é calculada deterministicamente.
/// Persiste numa task <c>phase='resumo_financeiro'</c>, <c>status='evaluated'</c>.
/// NÃO dispara recálculo de IVO/Score (não é etapa de validação).
/// </summary>
public class FinancialSummaryService : IFinancialSummaryService
{
    private const string Phase = "resumo_financeiro";

    private readonly Supabase.Client _supabase;
    private readonly ILlmFallbackService _llm;
    private readonly IStageSummaryService _stageSummaryService;
    private readonly IProjectService _projectService;
    private readonly IFinancialVariableService _financialVariableService;
    private readonly ILogger<FinancialSummaryService> _logger;

    public FinancialSummaryService(
        Supabase.Client supabase,
        ILlmFallbackService llm,
        IStageSummaryService stageSummaryService,
        IProjectService projectService,
        IFinancialVariableService financialVariableService,
        ILogger<FinancialSummaryService> logger)
    {
        _supabase = supabase;
        _llm = llm;
        _stageSummaryService = stageSummaryService;
        _projectService = projectService;
        _financialVariableService = financialVariableService;
        _logger = logger;
    }

    public async Task<FinancialSummaryDto> GenerateAsync(Guid projectId, Guid userId)
    {
        // 1) Gate: o Resumo Financeiro é liberado após a Etapa 4 (Modelo de Negócio).
        var etapa4 = await GetTaskByPhaseAsync(projectId, "etapa4");
        if (etapa4 == null || !string.Equals(etapa4.Status, "evaluated", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Conclua a Etapa 4 (Modelo de Negócio) antes de gerar o Resumo Financeiro.");

        // 2) Gerar a DRE via LLM a partir do contexto do projeto (Spec 022 v3 —
        //    DESVINCULADO da etapa 4; mesmo caminho do modo manual / AiFillAsync).
        //    Tolerante a falha: DreGenerator faz fallback para DRE zerada.
        var context = await BuildProjectContextAsync(projectId, userId);
        var (dreJson, _) = await DreGenerator.GenerateAsync(
            _llm, context, userId.ToString(), $"dre-generate:{projectId}", _logger);

        using var dreDoc = JsonDocument.Parse(dreJson);
        var dre = dreDoc.RootElement;

        // 3) Calcular a síntese.
        var sintese = DreCalculator.ComputeSintese(dre)
            ?? throw new InvalidOperationException("Não foi possível calcular a síntese a partir da DRE.");

        // 4) Montar o content da task resumo_financeiro: { dre, sintese }.
        var content = BuildContent(dre, sintese);

        // 5) Upsert: se já existe a task resumo_financeiro, atualiza; senão insere.
        var existing = await GetTaskByPhaseAsync(projectId, Phase);
        if (existing != null)
        {
            existing.Content = content;
            existing.Status = "evaluated";
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Project = null;
            existing.IaEvaluations = null;
            await _supabase.From<TaskModel>().Update(existing);
            _logger.LogInformation("[FinSummary] Resumo Financeiro atualizado para project {ProjectId}", projectId);
        }
        else
        {
            var model = new TaskModel
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = projectId.ToString(),
                Title = "Resumo Financeiro",
                Description = "Projeção para o primeiro ano",
                Phase = Phase,
                Content = content,
                Status = "evaluated",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Project = null,
                IaEvaluations = null,
            };
            await _supabase.From<TaskModel>().Insert(model);
            _logger.LogInformation("[FinSummary] Resumo Financeiro criado para project {ProjectId}", projectId);
        }

        sintese.FromCache = false;
        return sintese;
    }

    public async Task<FinancialSummaryDto?> GetExistingAsync(Guid projectId)
    {
        var task = await GetTaskByPhaseAsync(projectId, Phase);
        if (task?.Content == null) return null;

        // Recalcula SEMPRE a partir da DRE (fonte de verdade) — assim edições da
        // planilha no /financeiro refletem imediatamente no card, sem depender da
        // "sintese" salva (que pode estar desatualizada após uma edição).
        var dre = DreCalculator.TryExtractDre(task.Content);
        FinancialSummaryDto? sintese =
            dre != null ? DreCalculator.ComputeSintese(dre.Value) : ExtractSinteseFromContent(task.Content);
        if (sintese == null) return null;

        sintese.FromCache = true;
        return sintese;
    }

    public async Task<string?> GetDreContentAsync(Guid projectId)
    {
        var task = await GetTaskByPhaseAsync(projectId, Phase);
        return task?.Content;
    }

    public async Task<FinancialSummaryDto> AiFillAsync(Guid projectId, Guid userId)
    {
        // Guarda de USO ÚNICO: se já houve preenchimento por IA, recusa.
        var existing = await GetTaskByPhaseAsync(projectId, Phase);
        if (existing?.Content != null && HasAiFilledFlag(existing.Content))
            throw new InvalidOperationException("A DRE já foi preenchida por IA. Edite os valores manualmente.");

        // Monta contexto a partir do projeto + resumos das etapas (NÃO depende da etapa 4).
        var context = await BuildProjectContextAsync(projectId, userId);

        // Gera a DRE via LLM — tolerante a falha (fallback: DRE zerada).
        var (dreJson, _) = await DreGenerator.GenerateAsync(
            _llm, context, userId.ToString(), $"dre-aifill:{projectId}", _logger);

        using var dreDoc = JsonDocument.Parse(dreJson);
        var sintese = DreCalculator.ComputeSintese(dreDoc.RootElement)
            ?? new FinancialSummaryDto();

        // content = { dre, sintese, dre_ai_filled_at }.
        var content = BuildContentWithFlag(dreDoc.RootElement, sintese, DateTime.UtcNow);

        if (existing != null)
        {
            existing.Content = content;
            existing.Status = "evaluated";
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Project = null;
            existing.IaEvaluations = null;
            await _supabase.From<TaskModel>()
                .Filter("id", Operator.Equals, existing.Id)
                .Update(existing);
        }
        else
        {
            await _supabase.From<TaskModel>().Insert(new TaskModel
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = projectId.ToString(),
                Title = "Resumo Financeiro",
                Description = "Projeção para o primeiro ano",
                Phase = Phase,
                Content = content,
                Status = "evaluated",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Project = null,
                IaEvaluations = null,
            });
        }

        sintese.FromCache = false;
        return sintese;
    }

    /// <summary>Monta contexto (ideia + categoria + resumos das etapas) para a geração da DRE.</summary>
    private async Task<string> BuildProjectContextAsync(Guid projectId, Guid userId)
    {
        var parts = new List<string>();
        var project = await _projectService.GetByIdAsync(projectId, userId);
        if (!string.IsNullOrWhiteSpace(project?.Description)) parts.Add($"Ideia: {project!.Description}");
        if (!string.IsNullOrWhiteSpace(project?.Category)) parts.Add($"Categoria: {project!.Category}");
        try
        {
            var summaries = await _stageSummaryService.GetByProjectAsync(projectId);
            foreach (var s in summaries) parts.Add($"[{s.Stage}] {s.SummaryText}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinSummary] Falha ao montar contexto da DRE (continuando)");
        }

        // Spec 027 — modo `ai`: ancora a DRE nas variáveis financeiras oficiais (C3).
        // Modo `manual` (Spec 024) é texto livre; não ancora (sem regressão).
        try
        {
            if (await IsAiModeAsync(projectId))
            {
                var anchorBlock = await BuildOfficialVariablesBlockAsync(projectId);
                if (!string.IsNullOrEmpty(anchorBlock)) parts.Add(anchorBlock);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinSummary] Falha ao ancorar variáveis financeiras (continuando)");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "Sem contexto adicional disponível.";
    }

    /// <summary>Lê o creation_mode do projeto (default 'ai' se ausente).</summary>
    private async Task<bool> IsAiModeAsync(Guid projectId)
    {
        var resp = await _supabase
            .From<ProjectModel>()
            .Filter("id", Operator.Equals, projectId.ToString())
            .Limit(1)
            .Get();
        var mode = resp.Models.FirstOrDefault()?.CreationMode ?? "ai";
        return !string.Equals(mode, "manual", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Spec 027 — bloco "VALORES FINANCEIROS OFICIAIS" com as variáveis canônicas.
    /// A IA deve distribuir a DRE de modo que as MÉDIAS batam com estes valores.
    /// </summary>
    private async Task<string> BuildOfficialVariablesBlockAsync(Guid projectId)
    {
        var vars = await _financialVariableService.GetByProjectAsync(projectId);
        if (vars.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("VALORES FINANCEIROS OFICIAIS (ancore a DRE EXATAMENTE nestes — as médias mensais devem bater):");
        foreach (var v in vars)
        {
            var label = v.Key switch
            {
                "receita_mensal_media" => "Receita mensal média (distribua em receita_bruta[12], média ≈ este valor)",
                "custos_mensais"       => "Custos mensais (distribua entre cpv+opex[12], média ≈ este valor)",
                "margem_bruta_pct"     => "Margem bruta alvo (coerência cpv vs receita)",
                "break_even_meses"     => "Break-even (mês)",
                "cac"                  => "CAC (premissa, não vira linha)",
                "ltv"                  => "LTV (premissa, não vira linha)",
                "custo_desenvolvimento_total" => "Investimento inicial do MVP (one-off — NÃO some no opex mensal recorrente; apenas contexto)",
                _ => v.Key,
            };
            sb.AppendLine($"- {label}: {FormatValue(v.Value, v.Unit)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatValue(decimal value, string unit) => unit switch
    {
        "pct"   => $"{value:0.#}%",
        "meses" => $"{value:0.#} meses",
        "ratio" => $"{value:0.#}:1",
        "BRL/mês" => $"R$ {value:N2}/mês",
        _ => $"R$ {value:N2}",
    };

    private static bool HasAiFilledFlag(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("dre_ai_filled_at", out var f)
                   && f.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(f.GetString());
        }
        catch (JsonException) { return false; }
    }

    /// <summary>content com a flag de uso único: { dre, sintese, dre_ai_filled_at }.</summary>
    private static string BuildContentWithFlag(JsonElement dre, FinancialSummaryDto sintese, DateTime filledAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("dre");
            dre.WriteTo(writer);
            writer.WritePropertyName("sintese");
            JsonSerializer.Serialize(writer, sintese, SerializerOptions);
            writer.WriteString("dre_ai_filled_at", filledAt.ToString("o"));
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<TaskModel?> GetTaskByPhaseAsync(Guid projectId, string phase)
    {
        var resp = await _supabase
            .From<TaskModel>()
            .Filter("project_id", Operator.Equals, projectId.ToString())
            .Filter("phase", Operator.Equals, phase)
            .Order("created_at", Ordering.Descending)
            .Limit(1)
            .Get();
        return resp.Models.FirstOrDefault();
    }

    /// <summary>Monta o JSON do content: { dre: {...}, sintese: {...} }.</summary>
    private static string BuildContent(JsonElement dre, FinancialSummaryDto sintese)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("dre");
            dre.WriteTo(writer);
            writer.WritePropertyName("sintese");
            JsonSerializer.Serialize(writer, sintese, SerializerOptions);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static FinancialSummaryDto? ExtractSinteseFromContent(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("sintese", out var s) && s.ValueKind == JsonValueKind.Object)
                return s.Deserialize<FinancialSummaryDto>(SerializerOptions);
        }
        catch (JsonException) { }
        return null;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
