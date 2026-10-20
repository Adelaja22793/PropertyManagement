using PropertyManagement.Api.Middleware;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Api.Infrastructure;

public sealed class HttpRequestInfo(IHttpContextAccessor accessor) : IRequestInfo
{
    public string? CorrelationId => accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
