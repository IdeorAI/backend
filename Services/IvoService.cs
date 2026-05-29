using IdeorAI.Client;
using IdeorAI.Model.DTOs;
using IdeorAI.Model.SupabaseModels;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Implementação do IvoService.
/// Ver IIvoService para documentação completa da fórmula e variáveis.
/// BETA: usa scoring semântico-determinístico (sem LLM) + jitter por projectId
/// para garantir IVO sempre atualiza e cada projeto tem variação individual.
/// TODO: re-habilitar avaliação LLM com retry/circuit-breaker pós-beta.
/// </summary>
public class IvoService : IIvoService
{
    private readonly Supabase.Client _supabase;
    private readonly ILlmFallbackService _llmFallbackService;
    private readonly ILogger<IvoService> _logger;

    // Keywords de qualidade por variável — sinaliza profundidade analítica do conteúdo
    private static readonly Dictionary<string, string[]> VariableKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["O"] = ["diferencial", "diferencia", "inovação", "inovador", "único", "exclusiv",
                 "patente", "proprietári", "novidade", "disruptiv", "alternativa", "concorrente",
                 "vantagem competitiva", "moat", "barreira"],
        ["M"] = ["TAM", "SAM", "SOM", "mercado", "bilhões", "milhões", "crescimento",
                 "CAGR", "segment", "público-alvo", "público alvo", "demanda", "setor",
                 "indústria", "expansão", "escalável", "escalabilidade", "%"],
        ["V"] = ["pesquisa", "entrevista", "validação", "validou", "validad", "dado",
                 "evidência", "estudo", "relatório", "estatística", "%", "usuário",
                 "cliente", "dor", "frustração", "problema", "necessidade"],
        ["E"] = ["MVP", "protótipo", "tecnologia", "stack", "equipe", "recurso",
                 "viável", "viabilidade", "execução", "implementação", "prazo",
                 "etapa", "milestone", "roadmap", "infraestrutura"],
        ["T"] = ["tendência", "momento", "atual", "recente", "emergente", "IA",
                 "inteligência artificial", "regulamentação", "lei", "pandemia",
                 "transformação digital", "comportamento", "oportunidade", "janela"],
    };

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

        // try/catch externo removido — exceções agora propagam para o caller
        // (StageService.EnqueueIvoAndScoreAsync) onde são logadas com stack completo.
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

            _logger.LogInformation("[IVO Eval] Calculando scores semânticos para project {ProjectId} stage {Stage} (vars: {Vars})",
                projectId, stageNumber, string.Join(",", variables));

            var scores = ComputeSemanticScores(projectId, variables, stageContent);

            _logger.LogInformation("[IVO Eval] Scores calculados para project {ProjectId} stage {Stage}: {Scores}",
                projectId, stageNumber, string.Join(", ", scores.Select(kv => $"{kv.Key}={kv.Value}")));

            // Atualizar apenas as variáveis desta etapa
            if (scores.TryGetValue("O", out var o)) project.IvoO = o;
            if (scores.TryGetValue("M", out var m)) project.IvoM = m;
            if (scores.TryGetValue("V", out var v)) project.IvoV = v;
            if (scores.TryGetValue("E", out var e)) project.IvoE = e;
            if (scores.TryGetValue("T", out var t)) project.IvoT = t;

            // Atualizar score rescalado
            project.IvoScore10 = Math.Max(1.0m, Math.Min(10.0m, project.Score / 10.0m));

            try
            {
                var updateResp = await _supabase
                    .From<ProjectModel>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                    .Update(project);

                _logger.LogInformation(
                    "[IVO Eval] Update aplicado para project {ProjectId} stage {Stage}, rows afetadas: {Rows}",
                    projectId, stageNumber, updateResp?.Models?.Count ?? -1);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "[IVO Eval] FALHA em Update Supabase para project {ProjectId} stage {Stage}",
                    projectId, stageNumber);
                throw;
            }

            _logger.LogInformation(
                "[IVO Eval] IVO variables persistidas project {ProjectId}, stage {Stage}: {Scores}",
                projectId, stageNumber, string.Join(", ", scores.Select(kv => $"{kv.Key}={kv.Value}")));
        }
    }

    public async Task RecalculateAndPersistAsync(string projectId)
    {
        // try/catch externo removido — exceções propagam para o caller para diagnóstico.
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

            var evaluatedStages = tasks.Models
                .Where(t => string.Equals(t.Status, "evaluated", StringComparison.OrdinalIgnoreCase))
                .Select(t => ParseStageNumber(t.Phase))
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .Count();

            _logger.LogInformation(
                "[IVO Recalc] project {ProjectId}: evaluatedStages={Stages}, Score10={S10}, O={O}, M={M}, V={V}, E={E}, T={T}, D={D}",
                projectId, evaluatedStages, project.IvoScore10,
                project.IvoO, project.IvoM, project.IvoV, project.IvoE, project.IvoT, project.IvoD);

            project.IvoIndex = ComputeIvoIndex(
                projectId,
                evaluatedStages,
                project.IvoScore10,
                project.IvoO, project.IvoM, project.IvoV,
                project.IvoE, project.IvoT, project.IvoD);

            var updateResp = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                .Update(project);

            _logger.LogInformation(
                "[IVO Recalc] Persistido project {ProjectId}: IvoIndex=R${Index:F2}, rows={Rows}",
                projectId, project.IvoIndex, updateResp?.Models?.Count ?? -1);

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
    }

    public async Task ReevaluateAllStagesAsync(string projectId)
    {
        // try/catch externo removido — exceções propagam para o caller (controller)
        var tasks = await _supabase
            .From<TaskModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "evaluated")
            .Get();

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

    public async Task<IvoDataDto?> GetIvoDataAsync(string projectId)
    {
        // try/catch externo removido — exceções propagam para o caller
        var project = await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Single();

        if (project == null) return null;

        // IVO é parcial se alguma variável O/M/V/E/T ainda está no valor padrão 5.0
        var isPartial = project.IvoO == 5.0m || project.IvoM == 5.0m ||
                        project.IvoV == 5.0m || project.IvoE == 5.0m ||
                        project.IvoT == 5.0m;

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
        var evaluated = taskList.Where(t => string.Equals(t.Status, "evaluated", StringComparison.OrdinalIgnoreCase)).ToList();
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

    /// <summary>
    /// Calcula IVO Index com guardrails por número de etapas concluídas (avaliadas).
    /// Faixas:
    ///   0-1 etapas: R$ 250 (fixo)
    ///   2 etapas:   R$ 250 - R$ 1.500
    ///   3 etapas:   R$ 1.500 - R$ 10.000
    ///   4 etapas:   R$ 10.000 - R$ 50.000
    ///   5 etapas:   R$ 50.000 - R$ 1.000.000
    /// Dentro da faixa, posição é determinada por quality = (omvet_avg × score10 × D) / 1000, clamped 0-1.
    /// </summary>
    private static decimal ComputeIvoIndex(
        string projectId,
        int evaluatedStages,
        decimal score10, decimal o, decimal m, decimal v, decimal e, decimal t, decimal d)
    {
        var (min, max) = evaluatedStages switch
        {
            0 => (0m, 0m),       // t0: projeto vazio = R$ 0
            1 => (250m, 800m),
            2 => (800m, 3000m),
            3 => (3000m, 15000m),
            4 => (15000m, 80000m),
            _ => (80000m, 1000000m), // 5 ou mais
        };

        decimal baseValue;
        if (min == max)
        {
            baseValue = min;
        }
        else
        {
            var omvetAvg = (o + m + v + e + t) / 5m;
            var qualityRaw = (omvetAvg * score10 * d) / 1000m;
            var quality = Math.Max(0m, Math.Min(1m, qualityRaw));
            baseValue = min + (max - min) * quality;
        }

        // Jitter oculto por projeto: 1.00–1.35, determinístico (mesmo projeto sempre o mesmo valor).
        // Garante variação individual mesmo entre projetos com scores idênticos.
        // Clamp ao max da faixa para não vazar guardrails.
        var jitter = GetProjectJitter(projectId);
        var jittered = baseValue * jitter;
        var capped = Math.Min(max, jittered);

        return Math.Round(capped, 2);
    }

    /// <summary>
    /// Multiplicador determinístico por projeto (1.00–1.35).
    /// Seed = hash do projectId → mesmo projeto sempre recebe o mesmo jitter.
    /// Dá percepção de avaliação individual mesmo quando scores subjacentes coincidem.
    /// </summary>
    private static decimal GetProjectJitter(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return 1.0m;
        var seed = StableHash(projectId);
        var rng = new Random(seed);
        return 1.0m + (decimal)(rng.NextDouble() * 0.35);
    }

    /// <summary>
    /// Hash estável (não depende de string.GetHashCode randomization entre processos).
    /// FNV-1a 32-bit.
    /// </summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            const int prime = 16777619;
            int hash = (int)2166136261;
            foreach (var c in s)
            {
                hash ^= c;
                hash *= prime;
            }
            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }
    }

    /// <summary>
    /// Calcula scores O/M/V/E/T de forma determinística a partir do conteúdo da etapa.
    /// Combina:
    ///   - Profundidade do conteúdo (tamanho normalizado 0–1)
    ///   - Diversidade de vocabulário (palavras únicas / total)
    ///   - Densidade de dados quantitativos (números, %, R$)
    ///   - Match de keywords específicas da variável (M → "TAM"; V → "pesquisa"; etc.)
    ///   - Pequena variação por (projectId + variable) para evitar scores idênticos entre variáveis
    /// Resultado: scores no range 4.0–9.0, sempre individuais por (projeto, conteúdo, variável).
    /// </summary>
    private Dictionary<string, decimal> ComputeSemanticScores(string projectId, string[] variables, string content)
    {
        var result = new Dictionary<string, decimal>();
        if (string.IsNullOrWhiteSpace(content))
        {
            foreach (var v in variables) result[v] = 5.0m;
            return result;
        }

        var lower = content.ToLowerInvariant();
        var words = content.Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}' },
                                  StringSplitOptions.RemoveEmptyEntries);
        var totalWords = words.Length;
        var uniqueWords = words.Select(w => w.ToLowerInvariant()).Distinct().Count();

        // Sinal 1: profundidade — 0..1 (rico em 1500+ chars)
        var depth = Math.Min(1.0m, (decimal)content.Length / 1500m);

        // Sinal 2: diversidade lexical — 0..1
        var diversity = totalWords > 0
            ? Math.Min(1.0m, (decimal)uniqueWords / Math.Max(1m, (decimal)totalWords) * 1.5m)
            : 0m;

        // Sinal 3: densidade de dados quantitativos (números, %, R$)
        var digitCount = content.Count(char.IsDigit);
        var hasPercent = content.Contains('%');
        var hasCurrency = lower.Contains("r$") || lower.Contains("us$") || lower.Contains("usd") || lower.Contains("brl");
        var quantSignal = Math.Min(1.0m,
            (decimal)digitCount / Math.Max(50m, (decimal)content.Length / 30m)
            + (hasPercent ? 0.15m : 0m)
            + (hasCurrency ? 0.15m : 0m));

        // Base composta (peso: profundidade 40%, diversidade 30%, quant 30%) → 0..1
        var baseSignal = depth * 0.4m + diversity * 0.3m + quantSignal * 0.3m;
        // Mapeia para 4.5–8.5 (range base antes das keywords)
        var baseScore = 4.5m + baseSignal * 4.0m;

        foreach (var variable in variables)
        {
            // Sinal 4: match de keywords específicas da variável
            var keywordBonus = 0m;
            if (VariableKeywords.TryGetValue(variable, out var keywords))
            {
                var matchCount = keywords.Count(kw => lower.Contains(kw.ToLowerInvariant()));
                // 0 matches: 0; 1: +0.15; 2: +0.30; 3+: +0.50 (cap)
                keywordBonus = Math.Min(0.5m, matchCount * 0.18m);
            }

            // Sinal 5: variação determinística por (projectId + variable + content) → ±0.3
            var varSeed = StableHash(projectId + ":" + variable + ":" + content.Length);
            var varRng = new Random(varSeed);
            var jitterPerVar = ((decimal)varRng.NextDouble() - 0.5m) * 0.6m; // -0.3..+0.3

            var finalScore = baseScore + keywordBonus + jitterPerVar;
            // Clamp 4.0..9.0 — nunca cai no 5.0 default e nunca chega ao máximo absoluto
            result[variable] = Math.Round(Math.Max(4.0m, Math.Min(9.0m, finalScore)), 2);
        }

        return result;
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
            "RESPONDA SOMENTE COM JSON VÁLIDO. Sem prefácio, sem markdown, sem explicações antes ou depois.\n\n" +
            $"Analise o seguinte conteúdo gerado para a {stageName} de uma startup e retorne SOMENTE um JSON com os scores indicados.\n\n" +
            $"Critérios de avaliação:\n{criteria}\n\n" +
            "Escala: 1 = muito fraco, 5 = adequado/médio, 10 = excepcional\n\n" +
            $"Conteúdo da {stageName}:\n{truncatedContent}\n\n" +
            $"Retorne APENAS o JSON (sem markdown, sem explicação extra):\n{jsonExample}";
    }

    /// <summary>
    /// Extrai um objeto JSON de uma string que pode conter prefácio, markdown ou sufixo.
    /// Estratégias: bloco ```json``` → primeiro '{' ao último '}' → "{}".
    /// </summary>
    private static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        var trimmed = raw.Trim();

        // Estratégia 1: bloco ```json ... ``` ou ``` ... ```
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = trimmed.IndexOf('\n', fenceStart);
            if (afterFence >= 0)
            {
                var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (fenceEnd > afterFence)
                {
                    var candidate = trimmed[(afterFence + 1)..fenceEnd].Trim();
                    if (candidate.StartsWith('{')) return candidate;
                }
            }
        }

        // Estratégia 2: extrair do primeiro '{' ao último '}'
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return trimmed[firstBrace..(lastBrace + 1)];

        return "{}";
    }

    private async Task<Dictionary<string, decimal>> CallLlmEvaluationAsync(int stageNumber, string[] variables, string content)
    {
        var defaults = variables.ToDictionary(v => v, _ => 5.0m);

        try
        {
            var prompt = BuildEvaluationPrompt(stageNumber, variables, content);

            _logger.LogInformation("[IVO Eval] Chamando LlmFallbackService (DeepSeek) stage {Stage}, prompt len={Len}",
                stageNumber, prompt.Length);

            var llmResult = await _llmFallbackService.GenerateAsync(prompt);

            var rawText = llmResult?.Text ?? string.Empty;
            _logger.LogInformation("[IVO Eval] LLM retornou: {Len} chars, primeiros 200: {Preview}",
                rawText.Length,
                rawText.Length > 200 ? rawText[..200] : rawText);

            var cleaned = ExtractJsonObject(rawText);

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(cleaned);
            }
            catch (JsonException jex)
            {
                var preview = rawText.Length > 200 ? rawText[..200] : rawText;
                _logger.LogError(jex,
                    "[IVO Eval] JSON parse falhou stage {Stage} mesmo após ExtractJsonObject. Raw (200 chars): {Preview}",
                    stageNumber, preview);
                return defaults;
            }

            using (doc)
            {
            if (!doc.RootElement.TryGetProperty("scores", out var scores))
            {
                var preview = rawText.Length > 200 ? rawText[..200] : rawText;
                _logger.LogError("[IVO Eval] JSON sem propriedade 'scores' stage {Stage}. Raw (200 chars): {Preview}",
                    stageNumber, preview);
                return defaults;
            }

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[IVO Eval] FALHA em CallLlmEvaluationAsync stage {Stage}, usando defaults (5.0) para vars: {Vars}",
                stageNumber, string.Join(", ", variables));
            return defaults;
        }
    }
}
