using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

[Table("go_pivot_evaluations")]
public class GoPivotEvaluationModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("project_id")]
    public string ProjectId { get; set; } = "";

    [Column("verdict")]
    public string Verdict { get; set; } = "";

    [Column("confidence")]
    public int Confidence { get; set; }

    [Column("reasons")]
    public string ReasonsJson { get; set; } = "[]";

    [Column("pivot_recommendations")]
    public string? PivotRecommendationsJson { get; set; }

    [Column("override")]
    public bool Override { get; set; }

    [Column("invalidated_at")]
    public DateTimeOffset? InvalidatedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
