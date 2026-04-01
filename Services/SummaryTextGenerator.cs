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

        // Público
        if (json.TryGetProperty("publico_alvo", out var publico))
            parts.Add($"Público: {GetStringValue(publico)}");

        // Personas
        if (json.TryGetProperty("personas", out var personas) && personas.ValueKind == JsonValueKind.Array)
            parts.Add($"Personas: {personas.GetArrayLength()}");

        // Hipóteses
        if (json.TryGetProperty("hipoteses_valor", out var hipoteses) && hipoteses.ValueKind == JsonValueKind.Array)
            parts.Add($"Hipóteses: {hipoteses.GetArrayLength()}");

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
        if (json.TryGetProperty("tam", out var tam) && tam.TryGetProperty("valor", out var tamValor))
            parts.Add($"TAM: {GetStringValue(tamValor)}");

        // Concorrentes
        if (json.TryGetProperty("concorrentes", out var concorrentes) && concorrentes.ValueKind == JsonValueKind.Array)
            parts.Add($"Concorrentes: {concorrentes.GetArrayLength()}");

        // Oportunidade
        if (json.TryGetProperty("oportunidade_resumo", out var oportunidade))
            parts.Add($"Oportunidade: {GetStringValue(oportunidade)}");

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Etapa 3: Proposta de Valor
    /// Template: "Proposta: {frase}. Diferencial: {diferencial}. Métrica: {metrica}"
    /// </summary>
    private static string GenerateEtapa3(JsonElement json)
    {
        var parts = new List<string>();

        // Frase de valor
        if (json.TryGetProperty("frase_valor", out var frase))
            parts.Add($"Proposta: {GetStringValue(frase)}");

        // Diferencial
        if (json.TryGetProperty("diferencial_vs_alternativas", out var diferencial))
            parts.Add($"Diferencial: {GetStringValue(diferencial)}");

        // Métrica
        if (json.TryGetProperty("metrica_norteadora", out var metrica))
            parts.Add($"Métrica: {GetStringValue(metrica)}");

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
            if (canvas.TryGetProperty("fontes_receita", out var fontes) && 
                fontes.ValueKind == JsonValueKind.Array && 
                fontes.GetArrayLength() > 0)
            {
                var primeiraFonte = fontes[0];
                if (primeiraFonte.TryGetProperty("tipo", out var tipoReceita))
                    parts.Add($"Receita: {GetStringValue(tipoReceita)}");
            }

            // Canais
            if (canvas.TryGetProperty("canais", out var canais) && canais.ValueKind == JsonValueKind.Array)
                parts.Add($"Canais: {canais.GetArrayLength()}");
        }

        // Viabilidade
        if (json.TryGetProperty("viabilidade", out var viabilidade))
            parts.Add($"Viabilidade: {GetStringValue(viabilidade)}");

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
        if (json.TryGetProperty("funcionalidades_core", out var funcionalidades) && 
            funcionalidades.ValueKind == JsonValueKind.Array)
            parts.Add($"Funcionalidades: {funcionalidades.GetArrayLength()}");

        // Formatos
        if (json.TryGetProperty("formatos_mvp", out var formatos) && formatos.ValueKind == JsonValueKind.Array)
        {
            var formatosList = new List<string>();
            foreach (var formato in formatos.EnumerateArray())
                formatosList.Add(GetStringValue(formato));
            parts.Add($"Formato: {string.Join(", ", formatosList)}");
        }

        // Hipóteses
        if (json.TryGetProperty("hipoteses_teste", out var hipoteses) && hipoteses.ValueKind == JsonValueKind.Array)
            parts.Add($"Hipóteses: {hipoteses.GetArrayLength()}");

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
