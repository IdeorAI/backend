using Newtonsoft.Json.Linq;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

[Table("go_pivot_evaluations")]
public class GoPivotEvaluationModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    [Column("verdict")]
    public string Verdict { get; set; } = "";

    [Column("confidence")]
    public int Confidence { get; set; }

    [Column("reasons")]
    public JToken Reasons { get; set; } = new JArray();

    [Column("pivot_recommendations")]
    public JToken? PivotRecommendations { get; set; }

    [Column("positive_points")]
    public JToken PositivePoints { get; set; } = new JArray();

    [Column("improvement_points")]
    public JToken ImprovementPoints { get; set; } = new JArray();

    [Column("override")]
    public bool Override { get; set; }

    [Column("invalidated_at")]
    public DateTimeOffset? InvalidatedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
