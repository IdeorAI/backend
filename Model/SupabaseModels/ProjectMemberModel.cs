using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

[Table("project_members")]
public class ProjectMemberModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("project_id")]
    public string ProjectId { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("role")]
    public string Role { get; set; } = "viewer"; // "viewer" | "editor"

    [Column("status")]
    public string Status { get; set; } = "pending"; // "pending" | "accepted" | "declined"

    [Column("invited_by")]
    public string InvitedBy { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("accepted_at")]
    public DateTimeOffset? AcceptedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}
