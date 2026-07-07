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
            // A task resumo_financeiro NÃO é uma "Etapa" — entra como bloco oficial separado abaixo.
            if (string.Equals(t.Phase, "resumo_financeiro", StringComparison.OrdinalIgnoreCase)) continue;
            joined.AppendLine($"## Etapa {idx}");
            joined.AppendLine(t.Content);
            joined.AppendLine();
            idx++;
        }

        // Fonte de verdade financeira (Spec 022 v2): se houver Resumo Financeiro, injeta os
        // valores oficiais para a LLM usar EXATAMENTE estes números, sem reinventar.
        var financeiro = ordered.FirstOrDefault(t =>
            string.Equals(t.Phase, "resumo_financeiro", StringComparison.OrdinalIgnoreCase));
        var sintese = financeiro?.Content != null ? TryBuildSintese(financeiro.Content) : null;
        if (sintese != null)
        {
            joined.AppendLine(BuildOfficialFinancialBlock(sintese));
            joined.AppendLine();
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
            new LlmOptions(
                SkipCentralMetrics: false,
                UserId: userId,
                SourceContext: $"docsynth:{projectId}"),
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
                existingModel.OutdatedAt = null; // regenerado → volta a "atual"
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

    /// <summary>Lê a síntese do content da task resumo_financeiro, ou recalcula da DRE.</summary>
    private static Model.DTOs.FinancialSummaryDto? TryBuildSintese(string content)
    {
        var dre = DreCalculator.TryExtractDre(content);
        return dre != null ? DreCalculator.ComputeSintese(dre.Value) : null;
    }

    /// <summary>
    /// Bloco a ser injetado no prompt: instrui a LLM a usar EXATAMENTE estes números
    /// financeiros (fonte de verdade = DRE editada pelo usuário), sem reinventar.
    /// </summary>
    private static string BuildOfficialFinancialBlock(Model.DTOs.FinancialSummaryDto s)
    {
        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        // Formato anti-ambiguidade: valor numérico + extenso + escala explícita.
        // A LLM costuma reinterpretar "R$ 10.800" como "R$ 10,8 milhões" (o ponto de
        // milhar pt-BR vira gatilho de escala). Anexar o extenso e a escala remove o
        // gatilho e dá uma âncora textual difícil de "corromper".
        string Br(decimal v)
        {
            var num = v.ToString("C0", ptBr);                 // R$ 10.800
            var ext = ValorPorExtenso(v);                      // "dez mil e oitocentos reais"
            return $"{num} (exatamente {ext}; escala: {Escala(v)})";
        }
        var sb = new StringBuilder();
        sb.AppendLine("## VALORES FINANCEIROS OFICIAIS (use EXATAMENTE estes — não invente nem reescale)");
        sb.AppendLine("Projeção consolidada do primeiro ano (DRE validada pelo usuário):");
        sb.AppendLine($"- Receita Bruta (anual): {Br(s.ReceitaBrutaAnual)}");
        sb.AppendLine($"- Deduções e Impostos (anual): {Br(s.DeducoesAnual)}");
        sb.AppendLine($"- Receita Líquida (anual): {Br(s.ReceitaLiquidaAnual)}");
        sb.AppendLine($"- Lucro Bruto (anual): {Br(s.LucroBrutoAnual)}");
        sb.AppendLine($"- Despesas Operacionais (média mensal): {Br(s.OpexMensalMedia)}");
        sb.AppendLine($"- Lucro Líquido (anual): {Br(s.LucroLiquidoAnual)}");
        sb.AppendLine();
        sb.AppendLine("REGRAS OBRIGATÓRIAS ao citar qualquer número financeiro:");
        sb.AppendLine("1. Copie o valor EXATO acima — NUNCA converta a escala (ex.: NÃO transforme 'mil' em 'milhões', nem arredonde 'R$ 10.800' para 'R$ 10,8 milhões').");
        sb.AppendLine("2. O ponto é separador de MILHAR pt-BR: 'R$ 10.800' = dez mil e oitocentos reais, NÃO dez milhões e oitocentos mil.");
        sb.AppendLine("3. Se um valor parecer baixo para o seu senso comum, MANTENHA-O assim mesmo — ele foi validado pelo usuário e é autoritativo.");
        return sb.ToString();
    }

    /// <summary>Classifica a escala de um valor em reais (para reforço textual no prompt).</summary>
    private static string Escala(decimal v)
    {
        var a = Math.Abs(v);
        if (a >= 1_000_000_000m) return "bilhões de reais";
        if (a >= 1_000_000m) return "milhões de reais";
        if (a >= 1_000m) return "milhares de reais";
        return "reais (valor abaixo de mil)";
    }

    /// <summary>Extenso simplificado em pt-BR até bilhões — só a magnitude principal,
    /// suficiente para ancorar a escala e evitar reinterpretação da LLM.</summary>
    private static string ValorPorExtenso(decimal v)
    {
        var a = Math.Abs(Math.Round(v));
        if (a == 0) return "zero reais";
        if (a < 1_000m) return $"{a:0} reais";
        if (a < 1_000_000m)
        {
            var milhares = Math.Floor(a / 1_000m);
            var resto = a - milhares * 1_000m;
            return resto == 0 ? $"{milhares:0} mil reais" : $"{milhares:0} mil e {resto:0} reais";
        }
        if (a < 1_000_000_000m)
        {
            var milhoes = Math.Floor(a / 1_000_000m);
            var restoMil = Math.Floor((a - milhoes * 1_000_000m) / 1_000m);
            return restoMil == 0 ? $"{milhoes:0} milhões de reais" : $"{milhoes:0} milhões e {restoMil:0} mil reais";
        }
        var bilhoes = Math.Floor(a / 1_000_000_000m);
        return $"{bilhoes:0} bilhões de reais";
    }
}
