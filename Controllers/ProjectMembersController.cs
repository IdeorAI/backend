using IdeorAI.Model.DTOs;
using IdeorAI.Model.SupabaseModels;
using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId}/members")]
public class ProjectMembersController : ControllerBase
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ProjectMembersController> _logger;

    public ProjectMembersController(Supabase.Client supabase, ILogger<ProjectMembersController> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> IsOwnerAsync(Guid projectId, Guid userId)
    {
        var res = await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("owner_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Get();
        return res.Models.Count > 0;
    }

    private async Task<string?> GetMemberRoleAsync(Guid projectId, Guid userId)
    {
        var res = await _supabase
            .From<ProjectMemberModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "accepted")
            .Get();
        return res.Models.FirstOrDefault()?.Role;
    }

    // ── T-009: POST /api/projects/{id}/members/invite ─────────────────────

    [HttpPost("invite")]
    public async Task<IActionResult> InviteMember(
        Guid projectId,
        [FromBody] InviteMemberDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await IsOwnerAsync(projectId, userId))
            return StatusCode(403, new { error = "Apenas o dono do projeto pode convidar membros." });

        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { error = "Email é obrigatório." });

        if (dto.Role != "viewer" && dto.Role != "editor")
            return BadRequest(new { error = "Role deve ser 'viewer' ou 'editor'." });

        // Buscar usuário por email na tabela profiles
        var profileRes = await _supabase
            .From<ProfileModel>()
            .Filter("email", Supabase.Postgrest.Constants.Operator.Equals, dto.Email)
            .Get();

        var profile = profileRes.Models.FirstOrDefault();
        if (profile == null)
            return BadRequest(new { error = "Este email não está cadastrado na plataforma." });

        if (profile.Id == userId.ToString())
            return BadRequest(new { error = "Você não pode se convidar para seu próprio projeto." });

        // Verificar convite duplicado
        var existing = await _supabase
            .From<ProjectMemberModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, profile.Id)
            .Get();

        if (existing.Models.Any(m => m.Status == "pending" || m.Status == "accepted"))
            return BadRequest(new { error = "Já existe um convite pendente ou aceito para este usuário." });

        var member = new ProjectMemberModel
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId.ToString(),
            UserId = profile.Id,
            Role = dto.Role,
            Status = "pending",
            InvitedBy = userId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        await _supabase.From<ProjectMemberModel>().Insert(member);

        _logger.LogInformation("Member invited: project={ProjectId} invitee={InviteeId} role={Role}", projectId, profile.Id, dto.Role);
        return Ok(new { message = "Convite enviado com sucesso.", memberId = member.Id });
    }

    // ── T-010: GET /api/projects/{id}/members ────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetMembers(
        Guid projectId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await IsOwnerAsync(projectId, userId))
            return StatusCode(403, new { error = "Acesso negado." });

        var res = await _supabase
            .From<ProjectMemberModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Get();

        var members = res.Models
            .Where(m => m.Status != "declined")
            .Select(m => new ProjectMemberResponseDto
            {
                Id = m.Id,
                UserId = m.UserId,
                Email = "",   // enriched client-side via userId lookup if needed
                Name = "",
                Role = m.Role,
                Status = m.Status,
                InvitedAt = m.CreatedAt,
                AcceptedAt = m.AcceptedAt,
                IsExpired = m.Status == "pending" && m.ExpiresAt < DateTimeOffset.UtcNow
            })
            .ToList();

        return Ok(members);
    }

    // ── T-011: PUT /api/projects/{id}/members/{memberId} ─────────────────

    [HttpPut("{memberId}")]
    public async Task<IActionResult> UpdateRole(
        Guid projectId,
        string memberId,
        [FromBody] UpdateMemberRoleDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await IsOwnerAsync(projectId, userId))
            return StatusCode(403, new { error = "Acesso negado." });

        if (dto.Role != "viewer" && dto.Role != "editor")
            return BadRequest(new { error = "Role deve ser 'viewer' ou 'editor'." });

        var res = await _supabase
            .From<ProjectMemberModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, memberId)
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Get();

        var member = res.Models.FirstOrDefault();
        if (member == null) return NotFound(new { error = "Membro não encontrado." });

        member.Role = dto.Role;
        await _supabase.From<ProjectMemberModel>().Update(member);

        return Ok(new { message = "Role atualizado." });
    }

    // ── T-012: DELETE /api/projects/{id}/members/{memberId} ──────────────

    [HttpDelete("{memberId}")]
    public async Task<IActionResult> RemoveMember(
        Guid projectId,
        string memberId,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        if (!await IsOwnerAsync(projectId, userId))
            return StatusCode(403, new { error = "Acesso negado." });

        await _supabase
            .From<ProjectMemberModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, memberId)
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Delete();

        return NoContent();
    }

    // ── T-013: POST /api/projects/{id}/members/{memberId}/respond ─────────

    [HttpPost("{memberId}/respond")]
    public async Task<IActionResult> RespondInvite(
        Guid projectId,
        string memberId,
        [FromBody] RespondInviteDto dto,
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var res = await _supabase
            .From<ProjectMemberModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, memberId)
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Get();

        var member = res.Models.FirstOrDefault();
        if (member == null) return NotFound(new { error = "Convite não encontrado." });
        if (member.Status != "pending") return BadRequest(new { error = "Convite já respondido." });
        if (member.ExpiresAt < DateTimeOffset.UtcNow) return BadRequest(new { error = "Convite expirado." });

        member.Status = dto.Accept ? "accepted" : "declined";
        member.AcceptedAt = dto.Accept ? DateTimeOffset.UtcNow : null;
        await _supabase.From<ProjectMemberModel>().Update(member);

        return Ok(new { accepted = dto.Accept });
    }
}

// ── T-014: GET /api/projects/invites/pending — rota separada ─────────────────

[ApiController]
[Route("api/projects/invites")]
public class ProjectInvitesController : ControllerBase
{
    private readonly Supabase.Client _supabase;

    public ProjectInvitesController(Supabase.Client supabase) => _supabase = supabase;

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingInvites(
        [FromHeader(Name = "x-user-id")] Guid userId)
    {
        var res = await _supabase
            .From<ProjectMemberModel>()
            .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
            .Filter("status", Supabase.Postgrest.Constants.Operator.Equals, "pending")
            .Get();

        var pending = res.Models
            .Where(m => m.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(m => new PendingInviteDto
            {
                Id = m.Id,
                ProjectId = m.ProjectId,
                ProjectName = "",  // enriquecido client-side
                InviterName = "",
                InviterEmail = "",
                Role = m.Role,
                InvitedAt = m.CreatedAt,
                ExpiresAt = m.ExpiresAt
            })
            .ToList();

        return Ok(pending);
    }
}
