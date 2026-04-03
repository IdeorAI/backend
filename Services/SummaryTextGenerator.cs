using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Gera resumos de texto a partir do JSON da etapa (determinístico, sem API)
/// </summary>
public static class SummaryTextGenerator
{
    private const int MaxLength = 800;

    /// <summary>
    /// Gera summary_text a partir do JSON da etapa
    /// </summary>
    public static string Generate(string stage, JsonElement summaryJson)
    {
        return stage?.ToLower() switch
        {
            "etapa1" => GenerateEtapa1(summaryJson),
            "etapa2" => GenerateEtapa2(summaryJson),
            "etapa3" => GenerateEtapa3(summaryJson),
            "etapa4" => GenerateEtapa4(summaryJson),
            "etapa5" => GenerateEtapa5(summaryJson),
            _ => "Resumo não disponível"
        };
    }

    /// <summary>
    /// Etapa 1: Ideia e Problema
    /// Template: "Problema: {dor}. Público: {publico}. Personas: {count}. Hipóteses: {count}"
    /// </summary>
    private static string GenerateEtapa1(JsonElement json)
    {
        var parts = new List<string>();

        // Problema
        if (json.TryGetProperty("declaracao_problema", out var problema))
        {
            if (problema.TryGetProperty("dor_central", out var dor))
                parts.Add($"Problema: {GetStringValue(dor)}");
        }

        // Público (agora dentro de sintese ou nivel raiz)
        if (json.TryGetProperty("sintese", out var sintese))
        {
            if (sintese.TryGetProperty("publico_prioritario", out var publico))
                parts.Add($"Público: {GetStringValue(publico)}");
        }

        // Personas
        if (json.TryGetProperty("personas", out var personas) && personas.ValueKind == JsonValueKind.Array)
            parts.Add($"Personas: {personas.GetArrayLength()}");

        // Hipóteses (agora na sintese)
        if (json.TryGetProperty("sintese", out var sintese2))
        {
            if (sintese2.TryGetProperty("hipotese_monetizacao", out var hipotese))
                parts.Add($"Hipótese: {GetStringValue(hipotese)}");
        }

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Etapa 2: Pesquisa de Mercado
    /// Template: "TAM: {tam}. Concorrentes: {count}. Oportunidade: {oportunidade}"
    /// </summary>
    private static string GenerateEtapa2(JsonElement json)
    {
        var parts = new List<string>();

        // TAM
        if (json.TryGetProperty("dimensionamento_mercado", out var dim))
        {
            if (dim.TryGetProperty("tam", out var tam) && tam.TryGetProperty("valor", out var tamValor))
                parts.Add($"TAM: {GetStringValue(tamValor)}");
        }

        // Concorrentes
        if (json.TryGetProperty("analise_competitiva", out var comp))
        {
            if (comp.TryGetProperty("concorrentes_diretos", out var diretos) && diretos.ValueKind == JsonValueKind.Array)
                parts.Add($"Concorrentes Diretos: {diretos.GetArrayLength()}");
        }

        // Preço
        if (json.TryGetProperty("validacao_preco", out var preco))
        {
            if (preco.TryGetProperty("faixa_preco_sugerida", out var faixa))
                parts.Add($"Preço Sugerido: {GetStringValue(faixa)}");
        }

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Etapa 3: Proposta de Valor
    /// Template: "Proposta: {frase}. Diferencial: {diferencial}. Métrica: {metrica}"
    /// </summary>
    private static string GenerateEtapa3(JsonElement json)
    {
        var parts = new List<string>();

        // Frase de valor (Headline)
        if (json.TryGetProperty("proposta_valor_final", out var pvf))
        {
            if (pvf.TryGetProperty("headline", out var headline))
                parts.Add($"Proposta: {GetStringValue(headline)}");
        }

        // Diferencial
        if (json.TryGetProperty("posicionamento", out var pos))
        {
            if (pos.TryGetProperty("porque", out var razao))
                parts.Add($"Diferencial: {GetStringValue(razao)}");
        }

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Etapa 4: Modelo de Negócio
    /// Template: "Receita: {tipo}. Canais: {count}. Viabilidade: {viabilidade}"
    /// </summary>
    private static string GenerateEtapa4(JsonElement json)
    {
        var parts = new List<string>();

        // Fontes de receita
        if (json.TryGetProperty("business_model_canvas", out var canvas))
        {
            if (canvas.TryGetProperty("fluxos_receita", out var fluxos) && 
                fluxos.ValueKind == JsonValueKind.Array && 
                fluxos.GetArrayLength() > 0)
            {
                var primeiraFonte = fluxos[0];
                if (primeiraFonte.TryGetProperty("tipo", out var tipoReceita))
                    parts.Add($"Receita: {GetStringValue(tipoReceita)}");
            }
        }

        // Viabilidade (Break-even)
        if (json.TryGetProperty("projecao_financeira_simplificada", out var proj))
        {
            if (proj.TryGetProperty("ano_1", out var a1))
            {
                if (a1.TryGetProperty("break_even_months", out var be))
                    parts.Add($"Break-even: {GetStringValue(be)} meses");
            }
        }

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Etapa 5: Definição do MVP
    /// Template: "Funcionalidades: {count}. Formato: {formatos}. Hipóteses: {count}"
    /// </summary>
    private static string GenerateEtapa5(JsonElement json)
    {
        var parts = new List<string>();

        // Funcionalidades
        if (json.TryGetProperty("definicao_mvp", out var mvp))
        {
            if (mvp.TryGetProperty("core_features", out var features) && 
                features.ValueKind == JsonValueKind.Array)
                parts.Add($"Core Features: {features.GetArrayLength()}");
        }

        // Tempo/Roadmap
        if (json.TryGetProperty("roadmap_3_meses", out var roadmap) && roadmap.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"Roadmap: {roadmap.GetArrayLength()} meses");
        }

        // Stack
        if (json.TryGetProperty("stack_tecnologica", out var stack))
        {
            parts.Add("Stack definida");
        }

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Extrai valor string de um JsonElement
    /// </summary>
    private static string GetStringValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? "";
        return element.ToString() ?? "";
    }

    /// <summary>
    /// Trunca texto para o limite máximo
    /// </summary>
    private static string Truncate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (text.Length <= MaxLength)
            return text;

        return text.Substring(0, MaxLength - 3) + "...";
    }
}
