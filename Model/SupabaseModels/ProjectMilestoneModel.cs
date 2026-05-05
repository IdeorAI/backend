using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Marco individual de um projeto. Cada (project_id, milestone_key) é único.
/// Usado pelo ScoreService para individualizar o score (Opção B).
/// </summary>
[Table("project_milestones")]
public class ProjectMilestoneModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    [Column("user_id")]
    public string? UserId { get; set; }

    [Column("milestone_key")]
    public string MilestoneKey { get; set; } = null!;

    [Column("title")]
    public string Title { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    [Column("achieved_at")]
    public DateTime AchievedAt { get; set; }
}
