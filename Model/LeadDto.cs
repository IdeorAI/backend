namespace IdeorAI.Api.Model;

/// <summary>
/// DTO para captura de leads da landing page
/// </summary>
public class LeadDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

/// <summary>
/// Resposta da API de leads
/// </summary>
public class LeadResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? HubSpotContactId { get; set; }
}
