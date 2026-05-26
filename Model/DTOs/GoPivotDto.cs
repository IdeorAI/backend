namespace IdeorAI.Model.DTOs;

public class GoPivotResponseDto
{
    public Guid EvaluationId { get; set; }
    public string Verdict { get; set; } = "";
    public int Confidence { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<string>? PivotRecommendations { get; set; }
    public List<string> PositivePoints { get; set; } = [];
    public List<string> ImprovementPoints { get; set; } = [];
    public bool Override { get; set; }
    public bool FromCache { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int UsageCount { get; set; }
    public int UsageLimit { get; set; } = 3;
}

public class GoPivotLimitReachedException : Exception
{
    public GoPivotLimitReachedException(string message) : base(message) { }
}

public class GoPivotOverrideDto
{
    public bool Confirm { get; set; }
}
