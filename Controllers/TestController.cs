using Microsoft.AspNetCore.Mvc;
using IdeorAI.Services;

namespace IdeorAI.Controllers;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<TestController> _logger;

    public TestController(
        IProjectService projectService,
        Supabase.Client supabase,
        ILogger<TestController> logger)
    {
        _projectService = projectService;
        _supabase = supabase;
        _logger = logger;
    }

    [HttpGet("project/{projectId}/user/{userId}")]
    public async Task<IActionResult> TestProjectAccess(Guid projectId, Guid userId)
    {
        try
        {
            _logger.LogInformation("[TEST] Testing project access for project {ProjectId}, user {UserId}", projectId, userId);
            
            var project = await _projectService.GetByIdAsync(projectId, userId);
            
            if (project == null)
            {
                _logger.LogWarning("[TEST] Project NOT FOUND or access denied");
                return NotFound(new { 
                    error = "Project not found or access denied",
                    projectId = projectId.ToString(),
                    userId = userId.ToString()
                });
            }
            
            _logger.LogInformation("[TEST] Project found: {ProjectName}", project.Name);
            return Ok(new {
                projectId = project.Id,
                name = project.Name,
                ownerId = project.OwnerId,
                currentPhase = project.CurrentPhase,
                description = project.Description?.Substring(0, Math.Min(50, project.Description?.Length ?? 0))
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST] Exception during project access test");
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [HttpGet("supabase-connection")]
    public async Task<IActionResult> TestSupabaseConnection()
    {
        try
        {
            _logger.LogInformation("[TEST] Testing Supabase connection...");
            
            var response = await _supabase
                .From("projects")
                .Select("id,name")
                .Limit(1)
                .Get();
            
            var count = response.Models?.Count ?? 0;
            _logger.LogInformation("[TEST] Supabase connection OK. Projects count: {Count}", count);
            
            return Ok(new {
                status = "connected",
                projectsCount = count,
                firstProject = response.Models?.Count > 0 ? response.Models[0] : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TEST] Supabase connection FAILED");
            return StatusCode(500, new { 
                status = "failed",
                error = ex.Message,
                innerException = ex.InnerException?.Message
            });
        }
    }
}
