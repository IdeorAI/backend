using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

/// <summary>
/// Endpoint de diagnóstico — RESTRITO A AMBIENTE DE DESENVOLVIMENTO.
/// Em produção, todos os métodos retornam 404 NotFound.
/// </summary>
[ApiController]
[Route("api/debug")]
[ApiExplorerSettings(IgnoreApi = true)]
public class DebugController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public DebugController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        if (!_env.IsDevelopment())
            return NotFound();

        return Ok(new
        {
            supabaseUrlSet = !string.IsNullOrEmpty(_config["Supabase:Url"]),
            supabaseServiceKeySet = !string.IsNullOrEmpty(_config["Supabase:ServiceRoleKey"]),
        });
    }
}
