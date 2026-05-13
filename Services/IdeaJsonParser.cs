using System.Text.Json;

namespace IdeorAI.Services;

public static class IdeaJsonParser
{
    public static List<string> ParseIdeasJson(string raw, int expectedCount)
    {
        var cleaned = JsonSanitizer.ExtractJson(raw);
        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;
        var ideas = new List<string>();

        if (root.TryGetProperty("ideas", out var ideasEl))
        {
            foreach (var item in ideasEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    ideas.Add(item.GetString() ?? "");
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var subtitle = item.TryGetProperty("subtitle", out var s) ? s.GetString() ?? "" : "";
                    ideas.Add(string.IsNullOrEmpty(subtitle) ? title : $"{title} — {subtitle}");
                }
            }
        }
        return ideas;
    }

    public static List<string> ParseSimpleIdeasJson(string raw)
    {
        var cleaned = JsonSanitizer.ExtractJson(raw);
        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;
        var ideas = new List<string>();
        if (root.TryGetProperty("ideas", out var ideasEl))
            foreach (var item in ideasEl.EnumerateArray())
                ideas.Add(item.GetString() ?? "");
        return ideas;
    }
}
