namespace IdeorAI.Options;

public sealed class DeepSeekOptions
{
    public const string Section = "DeepSeek";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "deepseek-v4-flash";
    public int MaxTokens { get; set; } = 8000;
    public float Temperature { get; set; } = 0.7f;
    public int TimeoutSeconds { get; set; } = 60;
}
