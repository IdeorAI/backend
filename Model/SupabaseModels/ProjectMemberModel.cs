using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

[Table("project_members")]
public class ProjectMemberModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    [Column("user_id")]
    public string UserId { get; set; } = null!;

    [Column("role")]
    public string Role { get; set; } = "viewer"; // "viewer" | "editor"

    [Column("status")]
    public string Status { get; set; } = "pending"; // "pending" | "accepted" | "declined"

    [Column("invited_by")]
    public string InvitedBy { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("accepted_at")]
    public DateTimeOffset? AcceptedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}
