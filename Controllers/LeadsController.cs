using Microsoft.AspNetCore.Mvc;
using IdeorAI.Api.Model;
using IdeorAI.Api.Services;

namespace IdeorAI.Api.Controllers;

/// <summary>
/// Controller para captura de leads da landing page
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LeadsController : ControllerBase
{
    private readonly HubSpotService _hubSpotService;
    private readonly ILogger<LeadsController> _logger;

    public LeadsController(
        HubSpotService hubSpotService,
        ILogger<LeadsController> logger)
    {
        _hubSpotService = hubSpotService;
        _logger = logger;
    }

    /// <summary>
    /// Captura lead da landing page e envia para HubSpot
    /// </summary>
    /// <param name="lead">Dados do lead (nome, email, telefone)</param>
    /// <returns>Resposta com sucesso ou erro</returns>
    [HttpPost]
    [ProducesResponseType(typeof(LeadResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LeadResponseDto>> CaptureLeadAsync([FromBody] LeadDto lead)
    {
        _logger.LogInformation("Recebendo lead da landing page: {Email}", lead.Email);

        // Validação básica
        if (string.IsNullOrWhiteSpace(lead.Name))
        {
            return BadRequest(new LeadResponseDto
            {
                Success = false,
                Message = "Nome é obrigatório"
            });
        }

        if (string.IsNullOrWhiteSpace(lead.Email))
        {
            return BadRequest(new LeadResponseDto
            {
                Success = false,
                Message = "Email é obrigatório"
            });
        }

        if (string.IsNullOrWhiteSpace(lead.Phone))
        {
            return BadRequest(new LeadResponseDto
            {
                Success = false,
                Message = "Telefone é obrigatório"
            });
        }

        // Validação de email básica
        if (!lead.Email.Contains("@") || !lead.Email.Contains("."))
        {
            return BadRequest(new LeadResponseDto
            {
                Success = false,
                Message = "Email inválido"
            });
        }

        try
        {
            // Enviar para HubSpot
            var result = await _hubSpotService.CreateOrUpdateContactAsync(lead);

            if (result.Success)
            {
                _logger.LogInformation("Lead processado com sucesso: {Email} -> HubSpot ID: {ContactId}",
                    lead.Email, result.HubSpotContactId);
                return Ok(result);
            }
            else
            {
                _logger.LogWarning("Falha ao processar lead: {Email} - {Message}",
                    lead.Email, result.Message);
                return StatusCode(500, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar lead: {Email}", lead.Email);
            return StatusCode(500, new LeadResponseDto
            {
                Success = false,
                Message = "Erro interno ao processar lead. Por favor, tente novamente."
            });
        }
    }

    /// <summary>
    /// Health check do serviço de leads
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "healthy", service = "leads", timestamp = DateTime.UtcNow });
    }
}
