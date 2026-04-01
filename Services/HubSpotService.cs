using System.Text;
using System.Text.Json;
using IdeorAI.Api.Model;

namespace IdeorAI.Api.Services;

/// <summary>
/// Serviço para integração com HubSpot CRM
/// </summary>
public class HubSpotService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HubSpotService> _logger;
    private readonly string _accessToken;
    private readonly string _portalId;

    public HubSpotService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<HubSpotService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _accessToken = _configuration["HubSpot:AccessToken"]
            ?? throw new InvalidOperationException("HubSpot AccessToken não configurado");
        _portalId = _configuration["HubSpot:PortalId"]
            ?? throw new InvalidOperationException("HubSpot PortalId não configurado");

        // Configurar HttpClient
        _httpClient.BaseAddress = new Uri("https://api.hubapi.com");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
    }

    /// <summary>
    /// Cria ou atualiza um contato no HubSpot
    /// </summary>
    public async Task<LeadResponseDto> CreateOrUpdateContactAsync(LeadDto lead)
    {
        try
        {
            _logger.LogInformation("Enviando lead para HubSpot: {Email}", lead.Email);

            // Preparar dados do contato para HubSpot
            var contactData = new
            {
                properties = new
                {
                    email = lead.Email,
                    firstname = lead.Name,
                    phone = lead.Phone,
                    lifecyclestage = "lead",
                    hs_lead_status = "NEW"
                }
            };

            var json = JsonSerializer.Serialize(contactData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Tentar criar contato
            var response = await _httpClient.PostAsync("/crm/v3/objects/contacts", content);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                var contactId = result.GetProperty("id").GetString();

                _logger.LogInformation("Lead criado com sucesso no HubSpot. Contact ID: {ContactId}", contactId);

                return new LeadResponseDto
                {
                    Success = true,
                    Message = "Lead cadastrado com sucesso!",
                    HubSpotContactId = contactId
                };
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Contato já existe, tentar atualizar
                _logger.LogInformation("Contato já existe no HubSpot, tentando atualizar...");
                return await UpdateExistingContactAsync(lead);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Erro ao criar contato no HubSpot: {StatusCode} - {Error}",
                    response.StatusCode, error);

                return new LeadResponseDto
                {
                    Success = false,
                    Message = "Erro ao cadastrar lead. Por favor, tente novamente."
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao enviar lead para HubSpot");
            return new LeadResponseDto
            {
                Success = false,
                Message = "Erro interno ao processar lead."
            };
        }
    }

    /// <summary>
    /// Atualiza contato existente no HubSpot pelo email
    /// </summary>
    private async Task<LeadResponseDto> UpdateExistingContactAsync(LeadDto lead)
    {
        try
        {
            // Buscar contato pelo email
            var searchUrl = $"/crm/v3/objects/contacts/search";
            var searchBody = new
            {
                filterGroups = new[]
                {
                    new
                    {
                        filters = new[]
                        {
                            new
                            {
                                propertyName = "email",
                                @operator = "EQ",
                                value = lead.Email
                            }
                        }
                    }
                }
            };

            var searchJson = JsonSerializer.Serialize(searchBody);
            var searchContent = new StringContent(searchJson, Encoding.UTF8, "application/json");
            var searchResponse = await _httpClient.PostAsync(searchUrl, searchContent);

            if (!searchResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Erro ao buscar contato existente no HubSpot");
                return new LeadResponseDto
                {
                    Success = false,
                    Message = "Erro ao processar lead existente."
                };
            }

            var searchResult = await searchResponse.Content.ReadAsStringAsync();
            var searchData = JsonSerializer.Deserialize<JsonElement>(searchResult);
            var results = searchData.GetProperty("results");

            if (results.GetArrayLength() == 0)
            {
                return new LeadResponseDto
                {
                    Success = false,
                    Message = "Contato não encontrado para atualização."
                };
            }

            var contactId = results[0].GetProperty("id").GetString();

            // Atualizar contato
            var updateData = new
            {
                properties = new
                {
                    firstname = lead.Name,
                    phone = lead.Phone,
                    hs_lead_status = "NEW"
                }
            };

            var updateJson = JsonSerializer.Serialize(updateData);
            var updateContent = new StringContent(updateJson, Encoding.UTF8, "application/json");
            var updateResponse = await _httpClient.PatchAsync(
                $"/crm/v3/objects/contacts/{contactId}",
                updateContent);

            if (updateResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Lead atualizado com sucesso no HubSpot. Contact ID: {ContactId}", contactId);
                return new LeadResponseDto
                {
                    Success = true,
                    Message = "Lead atualizado com sucesso!",
                    HubSpotContactId = contactId
                };
            }
            else
            {
                var error = await updateResponse.Content.ReadAsStringAsync();
                _logger.LogError("Erro ao atualizar contato no HubSpot: {Error}", error);
                return new LeadResponseDto
                {
                    Success = false,
                    Message = "Erro ao atualizar lead."
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao atualizar lead no HubSpot");
            return new LeadResponseDto
            {
                Success = false,
                Message = "Erro ao atualizar lead existente."
            };
        }
    }
}
