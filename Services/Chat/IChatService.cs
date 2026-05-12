using IdeorAI.Model.DTOs;

namespace IdeorAI.Services.Chat;

public interface IChatService
{
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, string userId, CancellationToken ct);
    bool IsRateLimited(string userId);
}
