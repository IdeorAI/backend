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
