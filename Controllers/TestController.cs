using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IdeorAI.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public TestController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("gemini-direct")]
        public async Task<IActionResult> TestGeminiDirect()
        {
            var httpClient = _httpClientFactory.CreateClient();
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = "Gere uma ideia de negócio simples em uma frase" }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // Teste com header authentication
            var url = "https://generativelanguage.googleapis.com/v1/models/gemini-pro:generateContent";
            content.Headers.Add("x-goog-api-key", "AIzaSyDMu3DEtPa8HCvIHTybFEVjfdV4PNacKrY");
            
            try
            {
                var response = await httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                return Ok(new {
                    StatusCode = response.StatusCode,
                    Response = responseContent
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new {
                    Error = ex.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }
    }
}