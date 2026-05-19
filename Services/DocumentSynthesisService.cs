using IdeorAI.Client;
using IdeorAI.Model.SupabaseModels;
using System.Globalization;
using System.Text;

namespace IdeorAI.Services;

/// <summary>
/// Implementação do serviço de síntese de documentos finais (spec 019).
/// </summary>
public class DocumentSynthesisService : IDocumentSynthesisService
{
    private readonly Supabase.Client _supabase;
    private readonly ILlmFallbackService _llmService;
    private readonly ILogger<DocumentSynthesisService> _logger;

    public DocumentSynthesisService(
        Supabase.Client supabase,
        ILlmFallbackService llmService,
        ILogger<DocumentSynthesisService> logger)
    {
        _supabase = supabase;
        _llmService = llmService;
        _logger = logger;
    }

    private const string PitchDeckPrompt = """
Você é um consultor de pitch para startups. Construa um Pitch Deck estruturado de até 10 slides usando o conteúdo do projeto abaixo. Cada slide deve ter título + 3-5 bullets concisos.

Retorne APENAS markdown, sem prefácio ou explicação. Formato:

## Slide 1 — Problema
- bullet conciso
- bullet conciso

## Slide 2 — Solução
- bullet
...

[até Slide 10 — Oportunidade]

Slides obrigatórios: 1.Problema, 2.Solução, 3.Mercado, 4.Modelo de Negócio, 5.Diferenciais, 6.MVP, 7.Estratégia de Crescimento, 8.Equipe, 9.Roadmap, 10.Oportunidade.

Conteúdo do projeto:
{0}
""";

    private const string BusinessPlanPrompt = """
Você é um consultor de planejamento estratégico. Construa um Plano de Negócios detalhado e profissional a partir do conteúdo abaixo. Use markdown com 10 seções numeradas. Cada seção deve ter 2-4 parágrafos com substância e dados concretos do projeto.

Retorne APENAS markdown, sem prefácio. Formato:

## 1. Visão Geral do Negócio
... parágrafos ...

## 2. Análise de Mercado
... parágrafos ...

[até ## 10. Riscos e Oportunidades]

Seções obrigatórias: 1.Visão Geral, 2.Análise de Mercado, 3.Público-Alvo, 4.Proposta de Valor, 5.Modelo de Receita, 6.Estratégia de Entrada, 7.MVP, 8.Estrutura Operacional, 9.Projeções Iniciais, 10.Riscos e Oportunidades.

Conteúdo do projeto:
{0}
""";

    private const string ExecutiveSummaryPrompt = """
Você é um consultor para apresentar startups a investidores. Construa um Resumo Executivo de aproximadamente 1 página (300-500 palavras) consolidando o projeto abaixo.

Inclua headers curtos para: Problema, Solução, Mercado, Diferenciais, Estágio, Potencial. Termine com chamada para parceria/investimento.

Indicadores do projeto:
- IVO Index: R$ {0}
- Score: {1}/100

Conteúdo:
{2}

Retorne APENAS markdown, sem prefácio.
""";

    public Task<string> GeneratePitchDeckAsync(string projectId, string userId, CancellationToken ct)
        => GenerateInternalAsync(projectId, userId, "pitch-deck", PitchDeckPrompt, false, ct);

    public Task<string> GenerateBusinessPlanAsync(string projectId, string userId, CancellationToken ct)
        => GenerateInternalAsync(projectId, userId, "business-plan", BusinessPlanPrompt, false, ct);

    public Task<string> GenerateExecutiveSummaryAsync(string projectId, string userId, CancellationToken ct)
        => GenerateInternalAsync(projectId, userId, "executive-summary", ExecutiveSummaryPrompt, true, ct);

    private async Task<string> GenerateInternalAsync(
        string projectId,
        string userId,
        string docType,
        string promptTemplate,
        bool isExecutiveSummary,
        CancellationToken ct)
    {
        // 1) Buscar projeto e validar owner
        var project = await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Single();

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        if (!string.Equals(project.OwnerId, userId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("User is not owner of this project");

        // 2) Buscar tasks do projeto
        var tasksResp = await _supabase
            .From<TaskModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Order("phase", Supabase.Postgrest.Constants.Ordering.Ascending)
            .Get();

        var tasks = tasksResp.Models ?? new List<TaskModel>();
        var evaluatedCount = tasks.Count(t =>
            string.Equals(t.Status, "evaluated", StringComparison.OrdinalIgnoreCase));

        if (evaluatedCount < 5)
            throw new InvalidOperationException("Conclua as 5 etapas antes de gerar este documento");

        // 3) Construir joinedContent (etapa1..etapa5)
        var joined = new StringBuilder();
        var ordered = tasks
            .OrderBy(t => t.Phase ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int idx = 1;
        foreach (var t in ordered)
        {
            if (string.IsNullOrWhiteSpace(t.Content)) continue;
            joined.AppendLine($"## Etapa {idx}");
            joined.AppendLine(t.Content);
            joined.AppendLine();
            idx++;
        }

        var joinedContent = joined.ToString();

        // 4) Construir prompt
        string prompt;
        if (isExecutiveSummary)
        {
            var ivoFmt = project.IvoIndex.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
            var scoreFmt = project.Score.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
            prompt = string.Format(promptTemplate, ivoFmt, scoreFmt, joinedContent);
        }
        else
        {
            prompt = string.Format(promptTemplate, joinedContent);
        }

        // 5) Chamar LLM
        _logger.LogInformation("[DocSynthesis] Gerando {DocType} para project {ProjectId}", docType, projectId);
        var llmResult = await _llmService.GenerateAsync(
            prompt,
            new LlmOptions(SkipCentralMetrics: false),
            ct);

        var contentMd = llmResult.Text ?? string.Empty;

        // 6) Upsert em generated_documents
        await UpsertDocumentAsync(projectId, docType, contentMd, llmResult.ModelName ?? "unknown", ct);

        return contentMd;
    }

    private async Task UpsertDocumentAsync(string projectId, string docType, string contentMd, string modelUsed, CancellationToken ct)
    {
        try
        {
            var existing = await _supabase
                .From<GeneratedDocumentModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Filter("doc_type", Supabase.Postgrest.Constants.Operator.Equals, docType)
                .Get();

            var existingModel = existing.Models?.FirstOrDefault();

            if (existingModel != null)
            {
                existingModel.ContentMd = contentMd;
                existingModel.ModelUsed = modelUsed;
                existingModel.GeneratedAt = DateTime.UtcNow;
                await existingModel.Update<GeneratedDocumentModel>();
            }
            else
            {
                var row = new GeneratedDocumentModel
                {
                    ProjectId = projectId,
                    DocType = docType,
                    ContentMd = contentMd,
                    ModelUsed = modelUsed,
                    GeneratedAt = DateTime.UtcNow
                };
                await _supabase.From<GeneratedDocumentModel>().Insert(row);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DocSynthesis] Erro ao persistir documento {DocType} project {ProjectId}", docType, projectId);
            throw;
        }
    }
}
