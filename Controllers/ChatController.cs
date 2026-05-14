using IdeorAI.Model.DTOs;
using IdeorAI.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdeorAI.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(
    IChatService chatService,
    ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost]
    public async Task PostAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        var userId = Request.Headers["x-user-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new { error = "Usuário não autenticado" }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 500)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "Mensagem inválida (1–500 caracteres)" }, ct);
            return;
        }

        if (chatService.IsRateLimited(userId))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await Response.WriteAsJsonAsync(new { error = "Limite de 20 mensagens por hora atingido. Tente novamente em instantes." }, ct);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        logger.LogDebug("[ChatController] Iniciando stream para user={UserId}", userId);

        try
        {
            bool sentError422 = false;
            await foreach (var delta in chatService.StreamAsync(request, userId, ct))
            {
                if (delta.StartsWith("\x02DIFF\x02"))
                {
                    // Refine mode: emit a single diff event with isDiff flag
                    var diffPayload = delta[6..];
                    using var diffDoc = System.Text.Json.JsonDocument.Parse(diffPayload);
                    var diffJson = JsonSerializer.Serialize(new
                    {
                        isDiff = true,
                        diff = diffDoc.RootElement
                    });
                    var diffLine = Encoding.UTF8.GetBytes($"data: {diffJson}\n\n");
                    await Response.Body.WriteAsync(diffLine, ct);
                    await Response.Body.FlushAsync(ct);
                }
                else if (delta.StartsWith("\x02ERROR422\x02"))
                {
                    sentError422 = true;
                    var errorMessage = delta[10..];
                    var errJson = JsonSerializer.Serialize(new { error422 = errorMessage });
                    var errLine = Encoding.UTF8.GetBytes($"data: {errJson}\n\n");
                    await Response.Body.WriteAsync(errLine, ct);
                    await Response.Body.FlushAsync(ct);
                }
                else
                {
                    var json = JsonSerializer.Serialize(new { delta });
                    var line = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                    await Response.Body.WriteAsync(line, ct);
                    await Response.Body.FlushAsync(ct);
                }
            }

            if (!sentError422)
            {
                var doneBytes = Encoding.UTF8.GetBytes("data: {\"done\":true}\n\n");
                await Response.Body.WriteAsync(doneBytes, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // cliente desconectou — normal
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ChatController] Erro durante stream para user={UserId}", userId);
            var errBytes = Encoding.UTF8.GetBytes("data: {\"error\":\"Erro interno\"}\n\n");
            await Response.Body.WriteAsync(errBytes, ct);
        }
    }

    [HttpPost("refine")]
    public async Task<IActionResult> RefineAsync([FromBody] RefineRequest request, CancellationToken ct)
    {
        var userId = Request.Headers["x-user-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { error = "Usuário não autenticado" });

        if (string.IsNullOrWhiteSpace(request.UserFeedback) || request.UserFeedback.Length > 1000)
            return BadRequest(new { error = "Feedback inválido (1–1000 caracteres)" });

        if (string.IsNullOrWhiteSpace(request.StageContent))
            return BadRequest(new { error = "Conteúdo da etapa não informado" });

        if (chatService.IsRateLimited(userId))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "Limite de mensagens por hora atingido. Tente novamente em instantes." });

        logger.LogDebug("[ChatController.Refine] user={UserId} stage={Stage}", userId, request.StageName);

        var (sections, errorRaw) = await chatService.RefineDocumentAsync(request, userId, ct);

        if (sections is null)
        {
            var raw = errorRaw ?? string.Empty;
            var message = errorRaw == "connection_error"
                ? "Não foi possível conectar ao assistente. Tente novamente."
                : "A IA não retornou um diff válido. Tente reformular sua instrução.";
            return UnprocessableEntity(new RefineErrorResponse(message, raw));
        }

        return Ok(new RefineResponse(sections));
    }
}
