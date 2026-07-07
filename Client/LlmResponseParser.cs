using System.Text;
using System.Text.Json;

namespace IdeorAI.Client;

public static class LlmResponseParser
{
    public static string ExtractContent(JsonElement contentEl)
    {
        if (contentEl.ValueKind == JsonValueKind.Array)
        {
            // Minimax e alguns providers retornam [{type:"text", text:"..."}]
            var sb = new StringBuilder();
            foreach (var part in contentEl.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textPart))
                    sb.Append(textPart.GetString());
            }
            return sb.ToString();
        }

        return contentEl.GetString() ?? string.Empty;
    }

    /// <summary>
    /// Extrai o primeiro objeto JSON balanceado ({...}) embutido num texto livre.
    /// Usado quando o modelo "thinking" devolve content vazio e o JSON útil está no
    /// reasoning_content. Retorna "" se não houver um bloco {...} balanceado.
    /// </summary>
    public static string ExtractEmbeddedJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var start = text.IndexOf('{');
        if (start < 0) return string.Empty;

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return text[start..(i + 1)].Trim();
            }
        }
        return string.Empty; // chaves não fecharam (texto truncado)
    }

    public static string StripCodeFences(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```")) return t;

        var firstNewline = t.IndexOf('\n');
        if (firstNewline >= 0) t = t[(firstNewline + 1)..];
        if (t.EndsWith("```")) t = t[..^3].TrimEnd();
        return t;
    }
}
