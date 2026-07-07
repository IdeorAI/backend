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

        // Bloco de síntese: prompt MINI usa "sintese", prompt COMPLETO usa "resumo_ideia".
        // Aceita ambos para o summary funcionar nos dois modos.
        JsonElement resumo = default;
        var temResumo = json.TryGetProperty("sintese", out resumo)
                        || json.TryGetProperty("resumo_ideia", out resumo);

        // Público
        if (temResumo && resumo.TryGetProperty("publico_prioritario", out var publico))
            parts.Add($"Público: {GetStringValue(publico)}");

        // Personas
        if (json.TryGetProperty("personas", out var personas) && personas.ValueKind == JsonValueKind.Array)
            parts.Add($"Personas: {personas.GetArrayLength()}");

        // Hipótese: MINI = "hipotese_monetizacao"; COMPLETO = "hipotese_proposta_valor".
        if (temResumo)
        {
            if (resumo.TryGetProperty("hipotese_monetizacao", out var hipMon))
                parts.Add($"Hipótese: {GetStringValue(hipMon)}");
            else if (resumo.TryGetProperty("hipotese_proposta_valor", out var hipPv))
                parts.Add($"Hipótese: {GetStringValue(hipPv)}");
        }

        return Truncate(string.Join(". ", parts));
    }

    /// <summary>
    /// Etapa 2: Pesquisa de Mercado
    /// Schema real (JsonSanitizer): competidores_alternativas, gaps_exploraveis,
    /// posicionamento, metricas_mercado. (Antes apontava para dimensionamento_mercado/
    /// analise_competitiva/validacao_preco, que não existem — summary ficava vazio.)
    /// </summary>
    private static string GenerateEtapa2(JsonElement json)
    {
        var parts = new List<string>();

        // Posicionamento (frase principal)
        if (json.TryGetProperty("posicionamento", out var pos)
            && pos.TryGetProperty("frase", out var frase))
        {
            parts.Add($"Posicionamento: {GetStringValue(frase)}");
        }

        // Concorrentes (contagem de soluções reais)
        if (json.TryGetProperty("competidores_alternativas", out var comp)
            && comp.TryGetProperty("solucoes_reais", out var solucoes)
            && solucoes.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"Concorrentes: {solucoes.GetArrayLength()}");
        }

        // Gaps exploráveis (contagem)
        if (json.TryGetProperty("gaps_exploraveis", out var gaps)
            && gaps.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"Gaps: {gaps.GetArrayLength()}");
        }

        // Primeira métrica de mercado (TAM, tamanho do segmento)
        if (json.TryGetProperty("metricas_mercado", out var metricas)
            && metricas.ValueKind == JsonValueKind.Array
            && metricas.GetArrayLength() > 0
            && metricas[0].TryGetProperty("valor", out var metricaValor))
        {
            parts.Add($"Mercado: {GetStringValue(metricaValor)}");
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
