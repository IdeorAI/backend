namespace IdeorAI.Model.DTOs;

public class InviteMemberDto
{
    public string Email { get; set; } = "";
    public string Role { get; set; } = "viewer"; // "viewer" | "editor"
}

public class UpdateMemberRoleDto
{
    public string Role { get; set; } = "viewer";
}

public class RespondInviteDto
{
    public bool Accept { get; set; }
}

public class ProjectMemberResponseDto
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset InvitedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public bool IsExpired { get; set; }
}

public class PendingInviteDto
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string InviterName { get; set; } = "";
    public string InviterEmail { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTimeOffset InvitedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
