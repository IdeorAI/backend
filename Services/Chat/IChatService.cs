using IdeorAI.Model.DTOs;

namespace IdeorAI.Services.Chat;

public interface IChatService
{
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, string userId, CancellationToken ct);
    bool IsRateLimited(string userId);
    /// <summary>
    /// Verifica e incrementa o contador de rate-limit atomicamente.
    /// Retorna false se o usuário já estourou o limite (não incrementa nesse caso).
    /// </summary>
    bool TryConsumeRateLimit(string userId);
    Task<(Dictionary<string, string>? Sections, string? ErrorRaw)> RefineDocumentAsync(
        RefineRequest request, string userId, CancellationToken ct);
    Task<(string? RefinedContent, string? ErrorRaw)> RefineSectionAsync(
        RefineSectionRequest request, string userId, CancellationToken ct);
}
