using IdeorAI.Model.DTOs;

namespace IdeorAI.Services;

public interface IGoPivotService
{
    Task<GoPivotResponseDto?> GetExistingAsync(Guid projectId);
    Task<GoPivotResponseDto> EvaluateAsync(Guid projectId, Guid userId);
    Task InvalidateAsync(Guid projectId);
    Task ConfirmOverrideAsync(Guid projectId, Guid userId);
}
