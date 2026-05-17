namespace IdeorAI.Options;

public sealed class ChatOptions
{
    public const string Section = "Chat";

    public int RateLimitPerHour { get; set; } = 20;
}
