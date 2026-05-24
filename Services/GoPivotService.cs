using System.Text.Json;
using IdeorAI.Client;
using IdeorAI.Model.DTOs;
using IdeorAI.Model.SupabaseModels;

namespace IdeorAI.Services;

public class GoPivotService : IGoPivotService
{
    private readonly Supabase.Client _supabase;
    private readonly IStageSummaryService _stageSummaryService;
    private readonly ILlmFallbackService _llmFallbackService;
    private readonly ILogger<GoPivotService> _logger;

    private const string SystemPrompt = """
        Você é um avaliador de startups com 20 anos de experiência em Venture Capital.
        Sua função é ser BRUTALMENTE HONESTO e CRÍTICO. Não é seu trabalho ser gentil ou encorajador —
        é seu trabalho evitar que empreendedores percam tempo e dinheiro em ideias fracas.

        Avalie a ideia com base nos seguintes critérios:
        1. Tamanho e atratividade do mercado (TAM/SAM)
        2. Diferenciação competitiva real (não é suficiente "fazer melhor")
        3. Viabilidade técnica e de execução
        4. Clareza e sustentabilidade do modelo de receita
        5. Timing de mercado (cedo demais? tarde demais?)
        6. Riscos críticos não endereçados

        REGRAS OBRIGATÓRIAS:
        - Se a ideia tem problemas sérios em 2 ou mais critérios, o veredicto DEVE ser PIVOT.
        - Não use linguagem eufemística. Seja direto e específico.
        - confidence deve refletir genuinamente a força da ideia (0-100). Uma ideia média recebe no máximo 55.
        - pivotRecommendations deve ser ESPECÍFICO: não "mude o modelo de negócio", mas "foque em B2B em vez de B2C porque [motivo concreto]".
        - **Pontos positivos (positivePoints) — OBRIGATÓRIO no mínimo 3, máximo 5.** Mesmo quando o veredito for PIVOT, identifique no MÍNIMO 3 aspectos genuinamente positivos da ideia. Esses pontos têm propósito motivacional: o fundador precisa enxergar o que vale a pena preservar e usar como alavanca. Exemplos: clareza do problema descrito, especificidade do nicho, evidência de pesquisa, postura do fundador, escolha de canal, simplicidade técnica do MVP, alinhamento com tendências. NÃO use elogios genéricos ("ideia interessante", "boa intenção"). Sempre cite uma evidência concreta do conteúdo da etapa. Se você não conseguir encontrar 3 aspectos positivos genuínos baseados no que está escrito, retorne aspectos relacionados ao processo (ex: "fundador demonstrou disposição para validar antes de construir", "escopo do MVP está bem delimitado") — mas SEMPRE 3 mínimo.
        - Retorne entre 3 e 5 pontos em `improvementPoints` (acionáveis e priorizados).
        - Responda APENAS em JSON válido, sem markdown, sem texto fora do JSON.

        Formato de resposta:
        {
          "verdict": "GO" ou "PIVOT",
          "confidence": <inteiro 0-100>,
          "reasons": ["razão 1", "razão 2", "razão 3"],
          "pivotRecommendations": ["recomendação específica 1", "recomendação específica 2"],
          "positivePoints": ["ponto positivo concreto 1", "ponto positivo concreto 2", "ponto positivo concreto 3"],
          "improvementPoints": ["ponto a melhorar acionável 1", "ponto a melhorar acionável 2", "ponto a melhorar acionável 3"]
        }
        (pivotRecommendations deve ser omitido se verdict for GO; positivePoints e improvementPoints são SEMPRE obrigatórios)
        """;

    public GoPivotService(
        Supabase.Client supabase,
        IStageSummaryService stageSummaryService,
        ILlmFallbackService llmFallbackService,
        ILogger<GoPivotService> logger)
    {
        _supabase = supabase;
        _stageSummaryService = stageSummaryService;
        _llmFallbackService = llmFallbackService;
        _logger = logger;
    }

    public async Task<GoPivotResponseDto?> GetExistingAsync(Guid projectId)
    {
        var response = await _supabase.From<GoPivotEvaluationModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("invalidated_at", Supabase.Postgrest.Constants.Operator.Is, "null")
            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        var model = response.Models.FirstOrDefault();
        if (model == null) return null;

        return MapToDto(model, fromCache: true);
    }

    public async Task<GoPivotResponseDto> EvaluateAsync(Guid projectId, Guid userId)
    {
        var existing = await GetExistingAsync(projectId);
        if (existing != null) return existing;

        var summaries = await _stageSummaryService.GetByProjectAsync(projectId);
        var etapa1 = summaries.FirstOrDefault(s => s.Stage == "etapa1");
        var etapa2 = summaries.FirstOrDefault(s => s.Stage == "etapa2");

        if (etapa1 == null || etapa2 == null)
            throw new InvalidOperationException("Dados insuficientes: conclua as Etapas 1 e 2 antes de avaliar.");

        var prompt = BuildPrompt(etapa1.SummaryText ?? "", etapa2.SummaryText ?? "");
        var result = await CallLlmAsync(prompt);

        var parsed = ParseVerdict(result.Text);

        var model = new GoPivotEvaluationModel
        {
            ProjectId = projectId.ToString(),
            Verdict = parsed.Verdict,
            Confidence = parsed.Confidence,
            Reasons = Newtonsoft.Json.Linq.JArray.FromObject(parsed.Reasons ?? new List<string>()),
            PivotRecommendations = parsed.PivotRecommendations != null
                ? Newtonsoft.Json.Linq.JArray.FromObject(parsed.PivotRecommendations)
                : null,
            PositivePoints = Newtonsoft.Json.Linq.JArray.FromObject(parsed.PositivePoints ?? new List<string>()),
            ImprovementPoints = Newtonsoft.Json.Linq.JArray.FromObject(parsed.ImprovementPoints ?? new List<string>()),
        };

        await _supabase.From<GoPivotEvaluationModel>().Insert(model);

        _logger.LogInformation("[GoPivot] Verdict={Verdict} Confidence={Confidence} Project={ProjectId}",
            parsed.Verdict, parsed.Confidence, projectId);

        return MapToDto(model, fromCache: false);
    }

    public async Task InvalidateAsync(Guid projectId)
    {
        var response = await _supabase.From<GoPivotEvaluationModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("invalidated_at", Supabase.Postgrest.Constants.Operator.Is, "null")
            .Get();

        foreach (var eval in response.Models)
        {
            eval.InvalidatedAt = DateTimeOffset.UtcNow;
            await _supabase.From<GoPivotEvaluationModel>().Update(eval);
        }
    }

    public async Task ConfirmOverrideAsync(Guid projectId, Guid userId)
    {
        var response = await _supabase.From<GoPivotEvaluationModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("invalidated_at", Supabase.Postgrest.Constants.Operator.Is, "null")
            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        var eval = response.Models.FirstOrDefault()
            ?? throw new InvalidOperationException("Nenhuma avaliação encontrada para este projeto.");

        eval.Override = true;
        await _supabase.From<GoPivotEvaluationModel>().Update(eval);
    }

    private string BuildPrompt(string etapa1Text, string etapa2Text) =>
        $"{SystemPrompt}\n\nCONTEXTO DA IDEIA:\n\n=== ETAPA 1 — Problema e Oportunidade ===\n{etapa1Text}\n\n=== ETAPA 2 — Validação de Mercado ===\n{etapa2Text}";

    private Task<LlmResult> CallLlmAsync(string prompt)
        => _llmFallbackService.GenerateAsync(prompt);

    private static List<string> ReadStringArray(JsonElement root, string propertyName)
    {
        var list = new List<string>();
        if (root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                list.Add(item.GetString() ?? "");
            }
        }
        return list;
    }

    private static GoPivotResponseDto ParseVerdict(string raw)
    {
        var cleaned = JsonSanitizer.ExtractJson(raw);
        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;

        var verdict = root.GetProperty("verdict").GetString() ?? "PIVOT";
        if (verdict != "GO" && verdict != "PIVOT") verdict = "PIVOT";

        var confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetInt32() : 50;

        var reasons = ReadStringArray(root, "reasons");

        List<string>? pivotRecs = null;
        if (verdict == "PIVOT" && root.TryGetProperty("pivotRecommendations", out _))
            pivotRecs = ReadStringArray(root, "pivotRecommendations");

        var positivePoints = ReadStringArray(root, "positivePoints");
        var improvementPoints = ReadStringArray(root, "improvementPoints");

        return new GoPivotResponseDto
        {
            Verdict = verdict,
            Confidence = Math.Clamp(confidence, 0, 100),
            Reasons = reasons,
            PivotRecommendations = pivotRecs,
            PositivePoints = positivePoints,
            ImprovementPoints = improvementPoints,
        };
    }

    private static GoPivotResponseDto MapToDto(GoPivotEvaluationModel m, bool fromCache) => new()
    {
        EvaluationId = Guid.Parse(m.Id),
        Verdict = m.Verdict,
        Confidence = m.Confidence,
        Reasons = m.Reasons is Newtonsoft.Json.Linq.JArray rArr
            ? rArr.Select(t => t?.ToString() ?? "").ToList()
            : [],
        PivotRecommendations = m.PivotRecommendations is Newtonsoft.Json.Linq.JArray pArr
            ? pArr.Select(t => t?.ToString() ?? "").ToList()
            : null,
        PositivePoints = m.PositivePoints is Newtonsoft.Json.Linq.JArray posArr
            ? posArr.Select(t => t?.ToString() ?? "").ToList()
            : [],
        ImprovementPoints = m.ImprovementPoints is Newtonsoft.Json.Linq.JArray impArr
            ? impArr.Select(t => t?.ToString() ?? "").ToList()
            : [],
        Override = m.Override,
        FromCache = fromCache,
        CreatedAt = m.CreatedAt,
    };
}
