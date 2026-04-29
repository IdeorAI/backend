using IdeorAI.Client;
using IdeorAI.Model.DTOs;
using IdeorAI.Model.SupabaseModels;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Implementação do IvoService.
/// Ver IIvoService para documentação completa da fórmula e variáveis.
/// Usa ILlmFallbackService para avaliação — DeepSeek (primário) → OpenRouter → Gemini.
/// </summary>
public class IvoService : IIvoService
{
    private readonly Supabase.Client _supabase;
    private readonly ILlmFallbackService _llmFallbackService;
    private readonly ILogger<IvoService> _logger;

    // Variáveis IVO avaliadas por etapa
    private static readonly Dictionary<int, string[]> StageVariables = new()
    {
        { 1, ["O", "V"] },
        { 2, ["M", "T"] },
        { 3, ["O"] },
        { 4, ["E"] },
        { 5, ["E"] },
    };

    // Critérios por variável para o prompt de avaliação
    private static readonly Dictionary<string, string> VariableCriteria = new()
    {
        { "O", "O (Originalidade, 1-10): diferenciação da solução em relação ao mercado, grau de novidade, presença de alternativas similares" },
        { "V", "V (Validação da Dor, 1-10): evidências do problema com dados reais, validação com usuários, intensidade da dor" },
        { "M", "M (Potencial de Mercado, 1-10): TAM/SAM/SOM mencionados, escalabilidade, potencial de crescimento do setor" },
        { "T", "T (Timing de Mercado, 1-10): tendências atuais favoráveis, nível de saturação, urgência do problema no momento" },
        { "E", "E (Capacidade de Execução, 1-10): viabilidade prática da solução, complexidade técnica gerenciável, clareza do MVP, recursos necessários realistas" },
    };

    private static readonly Dictionary<int, string> StageNames = new()
    {
        { 1, "Problema e Oportunidade" },
        { 2, "Pesquisa de Mercado" },
        { 3, "Proposta de Valor" },
        { 4, "Modelo de Negócio" },
        { 5, "MVP" },
    };

    public IvoService(
        Supabase.Client supabase,
        ILlmFallbackService llmFallbackService,
        ILogger<IvoService> logger)
    {
        _supabase = supabase;
        _llmFallbackService = llmFallbackService;
        _logger = logger;
    }

    public async Task EvaluateStageAsync(string projectId, int stageNumber, string stageContent)
    {
        if (!StageVariables.TryGetValue(stageNumber, out var variables))
        {
            _logger.LogDebug("Stage {StageNumber} has no IVO variables to evaluate", stageNumber);
            return;
        }

        try
        {
            var project = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Single();

            if (project == null)
            {
                _logger.LogWarning("IVO evaluation: project {ProjectId} not found", projectId);
                return;
            }

            var scores = await CallGeminiEvaluationAsync(stageNumber, variables, stageContent);

            // Atualizar apenas as variáveis desta etapa
            if (scores.TryGetValue("O", out var o)) project.IvoO = o;
            if (scores.TryGetValue("M", out var m)) project.IvoM = m;
            if (scores.TryGetValue("V", out var v)) project.IvoV = v;
            if (scores.TryGetValue("E", out var e)) project.IvoE = e;
            if (scores.TryGetValue("T", out var t)) project.IvoT = t;

            // Atualizar score rescalado
            project.IvoScore10 = Math.Max(1.0m, Math.Min(10.0m, project.Score / 10.0m));

            await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Update(project);

            _logger.LogInformation(
                "IVO variables updated for project {ProjectId}, stage {Stage}: {Scores}",
                projectId, stageNumber, string.Join(", ", scores.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IVO EvaluateStageAsync for project {ProjectId}, stage {Stage}", projectId, stageNumber);
        }
    }

    public async Task RecalculateAndPersistAsync(string projectId)
    {
        try
        {
            var project = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Single();

            if (project == null)
            {
                _logger.LogWarning("IVO recalculate: project {ProjectId} not found", projectId);
                return;
            }

            var tasks = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Get();

            project.IvoScore10 = Math.Max(1.0m, Math.Min(10.0m, project.Score / 10.0m));
            project.IvoD = ComputeD(tasks.Models);
            project.IvoIndex = ComputeIvoIndex(
                project.IvoScore10,
                project.IvoO, project.IvoM, project.IvoV,
                project.IvoE, project.IvoT, project.IvoD);

            await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Update(project);

            // Gravar snapshot no histórico para o gráfico de evolução
            try
            {
                var snapshot = new IvoHistoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = projectId,
                    IvoIndex   = project.IvoIndex,
                    IvoScore10 = project.IvoScore10,
                    IvoO = project.IvoO,
                    IvoM = project.IvoM,
                    IvoV = project.IvoV,
                    IvoE = project.IvoE,
                    IvoT = project.IvoT,
                    IvoD = project.IvoD,
                    RecordedAt = DateTime.UtcNow,
                };
                await _supabase.From<IvoHistoryModel>().Insert(snapshot);
            }
            catch (Exception snapshotEx)
            {
                _logger.LogWarning(snapshotEx, "IVO snapshot insert failed for project {ProjectId} (non-blocking)", projectId);
            }

            _logger.LogInformation(
                "IVO recalculated for project {ProjectId}: D={D}, Index=R${Index:F0}",
                projectId, project.IvoD, project.IvoIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IVO RecalculateAndPersistAsync for project {ProjectId}", projectId);
        }
    }

    public async Task ReevaluateAllStagesAsync(string projectId)
    {
        try
        {
            var tasks = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "evaluated")
                .Get();

            // Agrupar tasks avaliadas por etapa e concatenar o conteúdo
            var stageGroups = tasks.Models
                .Where(t => !string.IsNullOrWhiteSpace(t.Content))
                .GroupBy(t => ParseStageNumber(t.Phase))
                .Where(g => g.Key.HasValue && StageVariables.ContainsKey(g.Key.Value))
                .ToList();

            if (!stageGroups.Any())
            {
                _logger.LogInformation("ReevaluateAllStages: no evaluated stages found for project {ProjectId}", projectId);
                await RecalculateAndPersistAsync(projectId);
                return;
            }

            foreach (var group in stageGroups)
            {
                var stageNumber = group.Key!.Value;
                var combinedContent = string.Join("\n\n", group.Select(t => t.Content));
                _logger.LogInformation("ReevaluateAllStages: evaluating stage {Stage} for project {ProjectId}", stageNumber, projectId);
                await EvaluateStageAsync(projectId, stageNumber, combinedContent);
            }

            await RecalculateAndPersistAsync(projectId);

            _logger.LogInformation("ReevaluateAllStages completed for project {ProjectId}: {StageCount} stages re-evaluated",
                projectId, stageGroups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ReevaluateAllStagesAsync for project {ProjectId}", projectId);
        }
    }

    public async Task<IvoDataDto?> GetIvoDataAsync(string projectId)
    {
        try
        {
            var project = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Single();

            if (project == null) return null;

            // IVO é parcial se alguma variável O/M/V/E/T ainda está em 1.0 (não avaliada pelo Gemini)
            var isPartial = project.IvoO <= 1.0m || project.IvoM <= 1.0m ||
                            project.IvoV <= 1.0m || project.IvoE <= 1.0m ||
                            project.IvoT <= 1.0m;

            var ivoValue = ComputeRawIvo(
                project.IvoScore10,
                project.IvoO, project.IvoM, project.IvoV,
                project.IvoE, project.IvoT, project.IvoD);

            return new IvoDataDto(
                ScoreIvo: project.IvoScore10,
                O: project.IvoO,
                M: project.IvoM,
                V: project.IvoV,
                E: project.IvoE,
                T: project.IvoT,
                D: project.IvoD,
                IvoValue: (decimal)ivoValue,
                IvoIndex: project.IvoIndex,
                IsPartial: isPartial
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IVO GetIvoDataAsync for project {ProjectId}", projectId);
            return null;
        }
    }

    // ─── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>Extrai o número da etapa a partir do campo phase (ex: "etapa1" → 1)</summary>
    private static int? ParseStageNumber(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase)) return null;
        var digits = new string(phase.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var num) ? num : null;
    }

    /// <summary>
    /// D = max(1.0, min(10.0, evaluatedStages * 1.5 + richStages * 0.3 + allCompleteBonus))
    /// </summary>
    private static decimal ComputeD(IEnumerable<TaskModel> tasks)
    {
        var taskList = tasks.ToList();
        var evaluated = taskList.Where(t => t.Status == "evaluated").ToList();
        var rich = evaluated.Count(t => (t.Content?.Length ?? 0) > 300);
        var bonus = evaluated.Count >= 5 ? 1.0m : 0.0m;
        var d = evaluated.Count * 1.5m + rich * 0.3m + bonus;
        return Math.Max(1.0m, Math.Min(10.0m, d));
    }

    // IVO = (Score10^1.3 × O × M × V × E × T × D) / 100_000
    private static double ComputeRawIvo(decimal score10, decimal o, decimal m, decimal v, decimal e, decimal t, decimal d)
    {
        return Math.Pow((double)score10, 1.3) * (double)(o * m * v * e * t * d) / 100_000.0;
    }

    // IVO Index (R$) = min(200 × (IVO + 1)^2.2, 1_000_000)
    private static decimal ComputeIvoIndex(decimal score10, decimal o, decimal m, decimal v, decimal e, decimal t, decimal d)
    {
        var ivo = ComputeRawIvo(score10, o, m, v, e, t, d);
        var ivoIndex = 200.0 * Math.Pow(ivo + 1.0, 2.2);
        return (decimal)Math.Min(ivoIndex, 1_000_000.0);
    }

    private string BuildEvaluationPrompt(int stageNumber, string[] variables, string content)
    {
        var stageName = StageNames.GetValueOrDefault(stageNumber, $"Etapa {stageNumber}");
        var criteria = string.Join("\n", variables.Select(v =>
            VariableCriteria.TryGetValue(v, out var c) ? $"- {c}" : $"- {v}: avalie de 1 a 10"));

        // Limitar conteúdo a ~2000 chars para economizar tokens
        var truncatedContent = content.Length > 2000
            ? content[..2000] + "\n[...conteúdo truncado para avaliação...]"
            : content;

        // Exemplo JSON esperado
        var exampleScores = string.Join(", ", variables.Select(v => $"\"{v}\": 7"));
        var exampleReasoning = string.Join(", ", variables.Select(v => $"\"{v}\": \"explicação curta\""));
        var jsonExample = "{\"scores\": {" + exampleScores + "}, \"reasoning\": {" + exampleReasoning + "}}";

        return
            $"Analise o seguinte conteúdo gerado para a {stageName} de uma startup e retorne SOMENTE um JSON com os scores indicados.\n\n" +
            $"Critérios de avaliação:\n{criteria}\n\n" +
            "Escala: 1 = muito fraco, 5 = adequado/médio, 10 = excepcional\n\n" +
            $"Conteúdo da {stageName}:\n{truncatedContent}\n\n" +
            $"Retorne APENAS o JSON (sem markdown, sem explicação extra):\n{jsonExample}";
    }

    private async Task<Dictionary<string, decimal>> CallGeminiEvaluationAsync(int stageNumber, string[] variables, string content)
    {
        var defaults = variables.ToDictionary(v => v, _ => 5.0m);

        try
        {
            var prompt = BuildEvaluationPrompt(stageNumber, variables, content);

            var llmResult = await _llmFallbackService.GenerateAsync(prompt);

            var cleaned = llmResult.Text
                .Replace("```json", "").Replace("```", "")
                .Trim();

            using var doc = JsonDocument.Parse(cleaned);
            var scores = doc.RootElement.GetProperty("scores");

            var result = new Dictionary<string, decimal>();
            foreach (var variable in variables)
            {
                if (scores.TryGetProperty(variable, out var el) && el.TryGetDecimal(out var score))
                {
                    // Garantir que o valor está no range 1-10
                    result[variable] = Math.Max(1.0m, Math.Min(10.0m, score));
                }
                else
                {
                    result[variable] = defaults[variable];
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IVO Gemini evaluation failed for stage {Stage}, using defaults (5.0) for variables: {Vars}",
                stageNumber, string.Join(", ", variables));
            return defaults;
        }
    }
}
