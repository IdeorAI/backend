using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdeorAI.Services;

/// <summary>
/// Helper para sanitização e validação de JSON retornado pela IA
/// Extrai JSON de markdown fences e valida campos obrigatórios por etapa
/// </summary>
public static class JsonSanitizer
{
    /// <summary>
    /// Extrai JSON de conteúdo que pode conter markdown fences (```json ... ```)
    /// </summary>
    public static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // Remove markdown fences ```json ... ``` ou ``` ... ```
        var match = Regex.Match(raw, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Tenta encontrar JSON diretamente (começa com { ou [)
        var startIndex = raw.IndexOfAny(new[] { '{', '[' });
        if (startIndex >= 0)
        {
            // Encontra o final correspondente
            var endChar = raw[startIndex] == '{' ? '}' : ']';
            var endIndex = raw.LastIndexOf(endChar);
            if (endIndex > startIndex)
                return raw[startIndex..(endIndex + 1)].Trim();
        }

        // Retorna o raw limpo como fallback
        return raw.Trim();
    }

    /// <summary>
    /// Tenta fazer parse do JSON e validar campos obrigatórios da etapa
    /// </summary>
    public static bool TryValidateSchema(string json, string stage, out JsonDocument? doc, out string errorMessage)
    {
        doc = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorMessage = "JSON vazio";
            return false;
        }

        // Tenta fazer parse
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errorMessage = $"JSON inválido: {ex.Message}";
            return false;
        }

        // Valida campos obrigatórios por etapa
        var root = doc.RootElement;
        var missingFields = new List<string>();

        var requiredFields = GetRequiredFieldsForStage(stage);

        foreach (var field in requiredFields)
        {
            if (!root.TryGetProperty(field, out _))
            {
                missingFields.Add(field);
            }
        }

        if (missingFields.Count > 0)
        {
            errorMessage = $"Campos obrigatórios ausentes: {string.Join(", ", missingFields)}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Retorna os campos obrigatórios para cada etapa
    /// </summary>
    private static string[] GetRequiredFieldsForStage(string stage)
    {
        return stage?.ToLower() switch
        {
            "etapa1" => new[] { "declaracao_problema", "personas", "sintese" },
            "etapa2" => new[] { "dimensionamento_mercado", "analise_competitiva", "tendencias", "validacao_preco" },
            "etapa3" => new[] { "value_proposition_canvas", "proposta_valor_final", "posicionamento" },
            "etapa4" => new[] { "business_model_canvas", "projecao_financeira_simplificada" },
            "etapa5" => new[] { "definicao_mvp", "roadmap_3_meses", "metricas_validacao", "stack_tecnologica", "custo_desenvolvimento" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Extrai e valida em uma única operação
    /// </summary>
    public static bool TryExtractAndValidate(string raw, string stage, out JsonDocument? doc, out string errorMessage)
    {
        var extracted = ExtractJson(raw);
        return TryValidateSchema(extracted, stage, out doc, out errorMessage);
    }
}
