using Microsoft.AspNetCore.Mvc;

namespace IdeorAI.Controllers;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<DebugController> _logger;

    public DebugController(IConfiguration config, ILogger<DebugController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var supabaseUrl = _config["Supabase:Url"];
        var supabaseAnonKey = _config["Supabase:AnonKey"];
        var supabaseServiceKey = _config["Supabase:ServiceRoleKey"];
        
        return Ok(new
        {
            supabaseUrl = supabaseUrl?.Substring(0, Math.Min(30, supabaseUrl?.Length ?? 0)) + "...",
            supabaseAnonKeySet = !string.IsNullOrEmpty(supabaseAnonKey),
            supabaseServiceKeySet = !string.IsNullOrEmpty(supabaseServiceKey),
            supabaseServiceKeyLength = supabaseServiceKey?.Length ?? 0,
            supabaseServiceKeyFirst30 = supabaseServiceKey?.Substring(0, Math.Min(30, supabaseServiceKey?.Length ?? 0)) + "...",
            envVars = new[]
            {
                $"Supabase__Url env: {Environment.GetEnvironmentVariable("Supabase__Url")?.Substring(0, Math.Min(30, Environment.GetEnvironmentVariable("Supabase__Url")?.Length ?? 0)) + "..."}",
                $"Supabase__ServiceRoleKey env: {Environment.GetEnvironmentVariable("Supabase__ServiceRoleKey")?.Substring(0, Math.Min(30, Environment.GetEnvironmentVariable("Supabase__ServiceRoleKey")?.Length ?? 0)) + "..."}"
            }
        });
    }
}
