using System.Text.Json;
using IdeorAI.Model.DTOs;

namespace IdeorAI.Services;

/// <summary>
/// Extrai a DRE (chave "dre") do JSON de conteúdo da etapa 4 e calcula a síntese
/// canônica (Spec 022 v2). Replica a fórmula client-side de <c>dre-table.tsx</c>:
/// os grupos "receita" e "opex" somam TODAS as linhas de entrada (inclui linhas
/// adicionadas pelo usuário); os totais seguem a cascata fixa da DRE.
/// Tolerante a JSON malformado/ausente — retorna null em vez de lançar.
/// </summary>
public static class DreCalculator
{
    private const int Meses = 12;

    /// <summary>
    /// Tenta extrair o elemento "dre" de uma string JSON (o <c>content</c> da etapa4).
    /// Retorna null se o content não for JSON válido ou não contiver "dre".
    /// </summary>
    public static JsonElement? TryExtractDre(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("dre", out var dre) &&
                dre.ValueKind == JsonValueKind.Object)
            {
                // Clona para sobreviver ao dispose do JsonDocument.
                return dre.Clone();
            }
        }
        catch (JsonException)
        {
            // content não é JSON válido — degradação graciosa.
        }
        return null;
    }

    /// <summary>
    /// Calcula a síntese (6 números) a partir do elemento "dre".
    /// Retorna null se não houver linhas utilizáveis.
    /// </summary>
    public static FinancialSummaryDto? ComputeSintese(JsonElement dre)
    {
        if (dre.ValueKind != JsonValueKind.Object ||
            !dre.TryGetProperty("linhas", out var linhas) ||
            linhas.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var receita = new decimal[Meses];   // soma do grupo "receita" (entradas)
        var opex = new decimal[Meses];       // soma do grupo "opex" (entradas)
        var deducoes = new decimal[Meses];
        var cpv = new decimal[Meses];
        var depreciacao = new decimal[Meses];
        var financeiro = new decimal[Meses];
        var imposto = new decimal[Meses];

        var anyLine = false;

        foreach (var l in linhas.EnumerateArray())
        {
            if (l.ValueKind != JsonValueKind.Object) continue;

            var tipo = GetString(l, "tipo");
            // Só linhas de ENTRADA contam (os totais são recalculados aqui).
            if (!string.Equals(tipo, "entrada", StringComparison.OrdinalIgnoreCase)) continue;

            var grupo = GetString(l, "grupo") ?? string.Empty;
            var id = GetString(l, "id") ?? string.Empty;
            var valores = GetValores(l);
            if (valores == null) continue;
            anyLine = true;

            switch (grupo.ToLowerInvariant())
            {
                case "receita":
                    Add(receita, valores);
                    break;
                case "opex":
                    Add(opex, valores);
                    break;
                case "deducao":
                    Add(deducoes, valores);
                    break;
                case "cpv":
                    Add(cpv, valores);
                    break;
                case "depreciacao":
                    Add(depreciacao, valores);
                    break;
                case "financeiro":
                    Add(financeiro, valores);
                    break;
                case "imposto":
                    Add(imposto, valores);
                    break;
                default:
                    // grupos desconhecidos (linhas extras do usuário sem grupo padrão):
                    // por id conhecido, encaixa; senão ignora no cálculo dos totais.
                    if (id == "deducoes") Add(deducoes, valores);
                    else if (id == "cpv") Add(cpv, valores);
                    break;
            }
        }

        if (!anyLine) return null;

        // Cascata mensal (idêntica ao frontend).
        decimal receitaBrutaAnual = 0, deducoesAnual = 0, receitaLiquidaAnual = 0,
                lucroBrutoAnual = 0, opexAnual = 0, lucroLiquidoAnual = 0;

        for (var m = 0; m < Meses; m++)
        {
            var receitaLiquida = receita[m] - deducoes[m];
            var lucroBruto = receitaLiquida - cpv[m];
            var ebitda = lucroBruto - opex[m];
            var ebit = ebitda - depreciacao[m];
            var lair = ebit + financeiro[m];
            var lucroLiquido = lair - imposto[m];

            receitaBrutaAnual += receita[m];
            deducoesAnual += deducoes[m];
            receitaLiquidaAnual += receitaLiquida;
            lucroBrutoAnual += lucroBruto;
            opexAnual += opex[m];
            lucroLiquidoAnual += lucroLiquido;
        }

        return new FinancialSummaryDto
        {
            ReceitaBrutaAnual = receitaBrutaAnual,
            DeducoesAnual = deducoesAnual,
            ReceitaLiquidaAnual = receitaLiquidaAnual,
            LucroBrutoAnual = lucroBrutoAnual,
            OpexMensalMedia = opexAnual / Meses,
            LucroLiquidoAnual = lucroLiquidoAnual,
        };
    }

    /// <summary>Uma linha da DRE pronta para exibição (descrição + 12 valores).</summary>
    public record DreLinhaView(string Descricao, string Id, string Tipo, decimal[] Valores);

    /// <summary>
    /// Retorna as linhas da DRE para renderização (ex.: PDF), com os TOTAIS já
    /// recalculados deterministicamente (idêntico ao frontend). Preserva a ordem
    /// e inclui linhas extras do usuário. Retorna null se não houver linhas.
    /// </summary>
    public static List<DreLinhaView>? BuildLinhasView(JsonElement dre)
    {
        if (dre.ValueKind != JsonValueKind.Object ||
            !dre.TryGetProperty("linhas", out var linhas) ||
            linhas.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var views = new List<DreLinhaView>();
        // Acumuladores para recalcular os totais.
        var receita = new decimal[Meses];
        var opex = new decimal[Meses];
        var deducoes = new decimal[Meses];
        var cpv = new decimal[Meses];
        var depreciacao = new decimal[Meses];
        var financeiro = new decimal[Meses];
        var imposto = new decimal[Meses];

        foreach (var l in linhas.EnumerateArray())
        {
            if (l.ValueKind != JsonValueKind.Object) continue;
            var tipo = GetString(l, "tipo") ?? "entrada";
            var grupo = (GetString(l, "grupo") ?? string.Empty).ToLowerInvariant();
            var id = GetString(l, "id") ?? string.Empty;
            var descricao = GetString(l, "descricao") ?? id;
            var valores = GetValores(l) ?? new decimal[Meses];

            if (string.Equals(tipo, "entrada", StringComparison.OrdinalIgnoreCase))
            {
                switch (grupo)
                {
                    case "receita": Add(receita, valores); break;
                    case "opex": Add(opex, valores); break;
                    case "deducao": Add(deducoes, valores); break;
                    case "cpv": Add(cpv, valores); break;
                    case "depreciacao": Add(depreciacao, valores); break;
                    case "financeiro": Add(financeiro, valores); break;
                    case "imposto": Add(imposto, valores); break;
                }
            }
            views.Add(new DreLinhaView(descricao, id, tipo, valores));
        }

        if (views.Count == 0) return null;

        // Recalcula os totais (cascata) e injeta de volta nas linhas "calculado".
        var receitaLiquida = new decimal[Meses];
        var lucroBruto = new decimal[Meses];
        var ebitda = new decimal[Meses];
        var ebit = new decimal[Meses];
        var lair = new decimal[Meses];
        var lucroLiquido = new decimal[Meses];
        for (var m = 0; m < Meses; m++)
        {
            receitaLiquida[m] = receita[m] - deducoes[m];
            lucroBruto[m] = receitaLiquida[m] - cpv[m];
            ebitda[m] = lucroBruto[m] - opex[m];
            ebit[m] = ebitda[m] - depreciacao[m];
            lair[m] = ebit[m] + financeiro[m];
            lucroLiquido[m] = lair[m] - imposto[m];
        }
        var totals = new Dictionary<string, decimal[]>
        {
            ["receita_liquida"] = receitaLiquida,
            ["lucro_bruto"] = lucroBruto,
            ["ebitda"] = ebitda,
            ["ebit"] = ebit,
            ["lair"] = lair,
            ["lucro_liquido"] = lucroLiquido,
        };
        for (var i = 0; i < views.Count; i++)
        {
            if (totals.TryGetValue(views[i].Id, out var vals))
                views[i] = views[i] with { Valores = vals };
        }
        return views;
    }

    private static void Add(decimal[] acc, decimal[] vals)
    {
        for (var m = 0; m < Meses; m++) acc[m] += vals[m];
    }

    private static string? GetString(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Lê o array "valores" garantindo 12 posições numéricas (preenche/trunca).</summary>
    private static decimal[]? GetValores(JsonElement line)
    {
        if (!line.TryGetProperty("valores", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var result = new decimal[Meses];
        var i = 0;
        foreach (var v in arr.EnumerateArray())
        {
            if (i >= Meses) break;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
                result[i] = d;
            // valores não-numéricos viram 0 (default do array).
            i++;
        }
        return result;
    }
}
