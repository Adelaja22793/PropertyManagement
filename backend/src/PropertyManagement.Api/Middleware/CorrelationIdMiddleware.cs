namespace PropertyManagement.Api.Middleware;

/// <summary>Gives every request a correlation ID, echoed in the response and written to logs and audit records.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var id = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsLetterOrDigit(c) || c == '-')
            ? incoming
            : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        context.Response.Headers[HeaderName] = id;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(context);
    }
}
