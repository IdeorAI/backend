// Middleware/RequestIdMiddleware.cs
public class RequestIdMiddleware
{
    private readonly RequestDelegate _next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<RequestIdMiddleware> logger)
    {
        var requestId = context.Request.Headers["x-request-id"].FirstOrDefault() 
            ?? Guid.NewGuid().ToString();
        
        // Adiciona ao contexto para uso em serviços
        context.Items["RequestId"] = requestId;
        
        // Configura escopo de logging com requestId
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId
        }))
        {
            await _next(context);
        }
    }
}

