using System.Globalization;
using System.Text.RegularExpressions;

namespace IdeorAI.Services;

/// <summary>
/// Spec 027 — normaliza strings financeiras das etapas em (decimal, unidade).
/// Determinístico, sem LLM. Retorna null quando não é possível extrair um número
/// confiável (faixas "R$ 5–10k", texto livre) — degradação graciosa.
/// </summary>
public static class FinancialValueParser
{
    public record ParsedValue(decimal Value, string Unit);

    /// <summary>
    /// Tenta extrair um valor monetário/numérico de uma string crua.
    /// <paramref name="hintUnit"/> é a unidade esperada para a chave (BRL, BRL/mês, pct, meses).
    /// </summary>
    public static ParsedValue? TryParse(string? raw, string hintUnit)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        // Faixas ("R$ 5 a 10 mil", "5–10k", "entre X e Y") → ambíguo, não grava.
        if (Regex.IsMatch(s, @"\d\s*(–|-|a|até|to|e)\s*\d", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(s, @":\s*\d"))  // exceção: ratio "5:1" tem '-' improvável; trata abaixo
        {
            // pode ser faixa OU ratio "5-1"; ratio é tratado por hintUnit=ratio adiante.
            if (hintUnit != "ratio") return null;
        }

        // Percentual.
        if (hintUnit == "pct" || s.Contains('%'))
        {
            var num = ExtractNumber(s.Replace("%", ""));
            return num.HasValue ? new ParsedValue(num.Value, "pct") : null;
        }

        // Ratio "N:1".
        if (hintUnit == "ratio" || Regex.IsMatch(s, @"^\s*\d+([.,]\d+)?\s*:\s*\d"))
        {
            var m = Regex.Match(s, @"(\d+([.,]\d+)?)\s*:\s*(\d+([.,]\d+)?)");
            if (m.Success)
            {
                var a = ParseDecimal(m.Groups[1].Value);
                var b = ParseDecimal(m.Groups[3].Value);
                if (a.HasValue && b.HasValue && b.Value != 0)
                    return new ParsedValue(a.Value / b.Value, "ratio");
            }
            return null;
        }

        // Meses ("3 meses", "3").
        if (hintUnit == "meses")
        {
            var num = ExtractNumber(s);
            return num.HasValue ? new ParsedValue(num.Value, "meses") : null;
        }

        // Monetário (default): "R$ 8.000,00", "R$ 8 mil", "8k/mês".
        var unit = s.Contains("/mês", StringComparison.OrdinalIgnoreCase) || hintUnit == "BRL/mês"
            ? "BRL/mês"
            : "BRL";
        var money = ExtractNumber(s);
        return money.HasValue ? new ParsedValue(money.Value, unit) : null;
    }

    /// <summary>Extrai um número com suporte a milhar pt-BR, decimal vírgula e sufixos k/mil/M/mi.</summary>
    private static decimal? ExtractNumber(string s)
    {
        s = s.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();

        // Sufixo de escala. A LLM frequentemente escreve sem espaço ("R$18mil",
        // "18k/mês"), então NÃO se pode exigir word boundary à esquerda (\bmil\b
        // falha em "8mil" porque "8m" não tem boundary). Aceita o sufixo colado ou
        // separado por espaço, exigindo só que termine em boundary à direita.
        // Ordem importa: testar milhão ANTES de milhar (senão "mi" casaria com nada
        // e "milhão" cairia no ramo de milhar por conter "mil").
        decimal scale = 1m;
        var lower = s.ToLowerInvariant();
        if (Regex.IsMatch(lower, @"\d\s*(mi|mm|milh(ão|ões|oes))\b") || lower.EndsWith("m"))
            scale = 1_000_000m;
        else if (Regex.IsMatch(lower, @"\d\s*(mil|k)\b") || lower.EndsWith("k"))
            scale = 1_000m;

        // Pega o primeiro grupo numérico (com . e , como separadores).
        var m = Regex.Match(s, @"-?\d[\d.,]*");
        if (!m.Success) return null;

        var d = ParseDecimal(m.Value);
        return d.HasValue ? d.Value * scale : null;
    }

    /// <summary>Converte "8.000,00" / "8000.00" / "8,5" para decimal de forma robusta.</summary>
    private static decimal? ParseDecimal(string token)
    {
        token = token.Trim();
        if (token.Length == 0) return null;

        var hasComma = token.Contains(',');
        var hasDot = token.Contains('.');

        string normalized;
        if (hasComma && hasDot)
        {
            // O último separador é o decimal; o outro é milhar.
            normalized = token.LastIndexOf(',') > token.LastIndexOf('.')
                ? token.Replace(".", "").Replace(",", ".")   // pt-BR: 8.000,00
                : token.Replace(",", "");                    // en: 8,000.00
        }
        else if (hasComma)
        {
            // Só vírgula → decimal pt-BR (8,5) OU milhar (8,000). Heurística: 3 dígitos após vírgula = milhar.
            var parts = token.Split(',');
            normalized = parts.Length == 2 && parts[1].Length == 3
                ? token.Replace(",", "")        // 8,000 = 8000
                : token.Replace(",", ".");      // 8,5 = 8.5
        }
        else if (hasDot)
        {
            // Só ponto → decimal en (8.5) OU milhar pt-BR (18.000 / 1.250.000).
            // Heurística pt-BR: separador de milhar quando há grupos de exatamente
            // 3 dígitos (ex.: "18.000", "1.250.000"). Caso contrário, é decimal.
            var parts = token.Split('.');
            var isThousands = parts.Length >= 2
                && parts.Skip(1).All(p => p.Length == 3)
                && parts[0].Length is >= 1 and <= 3;
            normalized = isThousands
                ? token.Replace(".", "")     // 18.000 = 18000
                : token;                     // 8.5 = 8.5
        }
        else
        {
            normalized = token; // só dígitos
        }

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
