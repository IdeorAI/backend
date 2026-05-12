namespace IdeorAI.Model.DTOs;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest
{
    public string Message { get; init; } = string.Empty;
    /// <summary>"guide" (default) or "refine"</summary>
    public string Mode { get; init; } = "guide";
    public string? ProjectId { get; init; }
    public int CurrentStageIndex { get; init; }
    public double? IvoScore { get; init; }
    public double? Score { get; init; }
    public string? GoPivotVerdict { get; init; }
    public string? ProjectName { get; init; }
    /// <summary>Conteúdo atual da etapa — preenchido apenas no modo "refine"</summary>
    public string? StageContent { get; init; }
    public string? StageName { get; init; }
    public List<ChatMessage> History { get; init; } = [];
}
