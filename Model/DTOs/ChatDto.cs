namespace IdeorAI.Model.DTOs;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest
{
    public string Message { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public int CurrentStageIndex { get; init; }
    public double? IvoScore { get; init; }
    public double? Score { get; init; }
    public string? GoPivotVerdict { get; init; }
    public string? ProjectName { get; init; }
    public List<ChatMessage> History { get; init; } = [];
}
