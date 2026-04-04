using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly IConfiguration _config;

    public DebugController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var supabaseUrl = _config["Supabase:Url"];
        var supabaseServiceKey = _config["Supabase:ServiceRoleKey"];
        
        return Ok(new
        {
            supabaseUrlSet = !string.IsNullOrEmpty(supabaseUrl),
            supabaseServiceKeySet = !string.IsNullOrEmpty(supabaseServiceKey),
            supabaseServiceKeyLength = supabaseServiceKey?.Length ?? 0,
        });
    }
}
