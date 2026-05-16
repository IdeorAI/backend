using IdeorAI.Model.DTOs;

namespace IdeorAI.Services.Chat;

public interface IChatService
{
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, string userId, CancellationToken ct);
    bool IsRateLimited(string userId);
    Task<(Dictionary<string, string>? Sections, string? ErrorRaw)> RefineDocumentAsync(
        RefineRequest request, string userId, CancellationToken ct);
    Task<(string? RefinedContent, string? ErrorRaw)> RefineSectionAsync(
        RefineSectionRequest request, string userId, CancellationToken ct);
}
