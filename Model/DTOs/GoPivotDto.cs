namespace IdeorAI.Model.DTOs;

public class GoPivotResponseDto
{
    public Guid EvaluationId { get; set; }
    public string Verdict { get; set; } = "";
    public int Confidence { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<string>? PivotRecommendations { get; set; }
    public bool Override { get; set; }
    public bool FromCache { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GoPivotOverrideDto
{
    public bool Confirm { get; set; }
}
