using System.Text;
using System.Text.Json;
using IdeorAI.Client;

namespace IdeorAI.Services;

/// <summary>
/// Spec 024 / Spec 022 v3 — geração da DRE por LLM, DESVINCULADA da etapa 4.
/// A LLM estima apenas os valores MENSAIS das linhas de ENTRADA; os totais
/// (cascata da DRE) são calculados deterministicamente aqui. Parsing tolerante
/// com FALLBACK para DRE zerada — nunca lança (lição "IVO sem LLM").
///
/// Reutilizável pelos dois modos: manual (botão "Preencher com IA") e, no
/// follow-up da 022, o modo IA (substituindo a extração da etapa 4).
/// </summary>
public static class DreGenerator
{
    private const int Meses = 12;

    // Linhas de entrada que a LLM preenche (id → descrição). Os totais (=) são derivados.
    private static readonly (string Id, string Descricao, string Tipo, string Grupo)[] Linhas =
    [
        ("receita_bruta",        "(=) RECEITA BRUTA",                       "entrada", "receita"),
        ("deducoes",             "(-) Deduções e Impostos sobre Vendas",    "entrada", "deducao"),
        ("receita_liquida",      "(=) RECEITA LÍQUIDA",                     "calculado", "total"),
        ("cpv",                  "(-) CPV (Custo de Produto Vendido)",      "entrada", "cpv"),
        ("lucro_bruto",          "(=) LUCRO BRUTO",                         "calculado", "total"),
        ("opex",                 "(-) Despesas Operacionais (OPEX)",        "entrada", "opex"),
        ("ebitda",               "(=) EBITDA / LAJIDA",                     "calculado", "total"),
        ("depreciacao",          "(-) Depreciação e Amortização",          "entrada", "depreciacao"),
        ("ebit",                 "(=) EBIT / LAJ",                          "calculado", "total"),
        ("resultado_financeiro", "(+/-) Resultado Financeiro",             "entrada", "financeiro"),
        ("lair",                 "(=) LAIR",                                "calculado", "total"),
        ("irpj_csll",            "(-) IRPJ e CSLL",                         "entrada", "imposto"),
        ("lucro_liquido",        "(=) LUCRO LÍQUIDO",                       "calculado", "total"),
    ];

    // Linhas que a LLM estima (entradas com impacto monetário direto).
    private static readonly string[] InputIds =
        ["receita_bruta", "deducoes", "cpv", "opex", "depreciacao", "resultado_financeiro", "irpj_csll"];

    /// <summary>Monta o prompt de geração da DRE a partir do contexto do projeto.</summary>
    public static string BuildPrompt(string projectContext)
    {
        var ids = string.Join(", ", InputIds.Select(i => $"\"{i}\""));
        return
            "Você é um analista financeiro. Com base no contexto do projeto abaixo, estime uma " +
            "projeção financeira mensal realista para os 12 PRIMEIROS MESES de operação.\n\n" +
            "Responda APENAS com um JSON válido (sem markdown, sem comentários) no formato:\n" +
            "{ \"linhas\": { " +
            "\"receita_bruta\": [12 números], \"deducoes\": [12], \"cpv\": [12], \"opex\": [12], " +
            "\"depreciacao\": [12], \"resultado_financeiro\": [12], \"irpj_csll\": [12] } }\n\n" +
            $"Regras: cada array tem EXATAMENTE 12 números (R$, sem separador de milhar, ponto decimal). " +
            $"Use as chaves exatamente: {ids}. Custos/deduções/impostos são positivos (serão subtraídos). " +
            "Valores coerentes com o estágio inicial (receita começa baixa e cresce). NÃO inclua os totais.\n" +
            "CUSTOS FIXOS ENXUTOS: trate-se de uma startup no INÍCIO da operação, com estrutura mínima " +
            "e enxuta. Mantenha os custos fixos (parte de 'opex' e 'depreciacao') nos MENORES valores " +
            "realistas possíveis — sem escritório caro, time grande ou despesas supérfluas; priorize " +
            "trabalho enxuto, ferramentas essenciais e gastos variáveis sobre fixos. Seja realista e " +
            "conservador, não otimista.\n\n" +
            $"CONTEXTO DO PROJETO:\n{projectContext}";
    }

    /// <summary>
    /// Tenta gerar a DRE via LLM. Em QUALQUER falha (LLM, parse, validação),
    /// retorna a DRE zerada — nunca lança. Retorna (dreJson, aiSucceeded).
    /// </summary>
    public static async Task<(string DreJson, bool AiSucceeded)> GenerateAsync(
        ILlmFallbackService llm,
        string projectContext,
        string? userId,
        string sourceContext,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var result = await llm.GenerateAsync(
                BuildPrompt(projectContext),
                new LlmOptions(UserId: userId, SourceContext: sourceContext),
                ct);

            var inputs = ParseInputs(result.Text);
            if (inputs == null)
            {
                logger.LogWarning("[DreGenerator] LLM retornou JSON não parseável — usando DRE zerada.");
                return (BuildDreJson(new Dictionary<string, decimal[]>()), false);
            }
            return (BuildDreJson(inputs), true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DreGenerator] Falha na geração da DRE — usando DRE zerada.");
            return (BuildDreJson(new Dictionary<string, decimal[]>()), false);
        }
    }

    /// <summary>DRE 13×12 toda zerada (estado inicial do modo manual).</summary>
    public static string BuildZeroedDreJson() => BuildDreJson(new Dictionary<string, decimal[]>());

    // ── Parsing tolerante dos valores estimados pela LLM ──────────────────────
    private static Dictionary<string, decimal[]>? ParseInputs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var json = StripFences(raw);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Aceita { linhas: {...} } ou o objeto direto.
            var obj = root.TryGetProperty("linhas", out var l) && l.ValueKind == JsonValueKind.Object
                ? l
                : root;
            if (obj.ValueKind != JsonValueKind.Object) return null;

            var map = new Dictionary<string, decimal[]>();
            foreach (var id in InputIds)
            {
                if (obj.TryGetProperty(id, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    map[id] = ToMonths(arr);
            }
            return map.Count > 0 ? map : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal[] ToMonths(JsonElement arr)
    {
        var vals = new decimal[Meses];
        var i = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (i >= Meses) break;
            vals[i++] = el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d) ? d : 0m;
        }
        return vals;
    }

    private static string StripFences(string s)
    {
        var t = s.Trim();
        if (t.StartsWith("```"))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
        }
        // Recorta do primeiro { ao último } para tolerar texto ao redor.
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        return start >= 0 && end > start ? t[start..(end + 1)] : t.Trim();
    }

    // ── Montagem do DreData completo (mesmo shape de dre-table.tsx) ────────────
    private static string BuildDreJson(Dictionary<string, decimal[]> inputs)
    {
        decimal[] V(string id) => inputs.TryGetValue(id, out var v) ? v : new decimal[Meses];

        var receita = V("receita_bruta");
        var deducoes = V("deducoes");
        var cpv = V("cpv");
        var opex = V("opex");
        var depreciacao = V("depreciacao");
        var financeiro = V("resultado_financeiro");
        var irpj = V("irpj_csll");

        // Cascata determinística (idêntica ao recalcular() de dre-table.tsx).
        var receitaLiquida = Sub(receita, deducoes);
        var lucroBruto = Sub(receitaLiquida, cpv);
        var ebitda = Sub(lucroBruto, opex);
        var ebit = Sub(ebitda, depreciacao);
        var lair = Add(ebit, financeiro);
        var lucroLiquido = Sub(lair, irpj);

        var byId = new Dictionary<string, decimal[]>
        {
            ["receita_bruta"] = receita,
            ["deducoes"] = deducoes,
            ["receita_liquida"] = receitaLiquida,
            ["cpv"] = cpv,
            ["lucro_bruto"] = lucroBruto,
            ["opex"] = opex,
            ["ebitda"] = ebitda,
            ["depreciacao"] = depreciacao,
            ["ebit"] = ebit,
            ["resultado_financeiro"] = financeiro,
            ["lair"] = lair,
            ["irpj_csll"] = irpj,
            ["lucro_liquido"] = lucroLiquido,
        };

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WritePropertyName("linhas");
            w.WriteStartArray();
            foreach (var (id, descricao, tipo, grupo) in Linhas)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("descricao", descricao);
                w.WriteString("tipo", tipo);
                w.WriteString("grupo", grupo);
                w.WritePropertyName("valores");
                w.WriteStartArray();
                foreach (var v in byId[id]) w.WriteNumberValue(v);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static decimal[] Sub(decimal[] a, decimal[] b)
    {
        var r = new decimal[Meses];
        for (var i = 0; i < Meses; i++) r[i] = a[i] - b[i];
        return r;
    }

    private static decimal[] Add(decimal[] a, decimal[] b)
    {
        var r = new decimal[Meses];
        for (var i = 0; i < Meses; i++) r[i] = a[i] + b[i];
        return r;
    }
}
