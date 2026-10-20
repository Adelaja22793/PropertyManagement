using System.Security.Claims;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue(ClaimTypes.Name);

    public Guid RequireUserId() => UserId ?? throw new UnauthorizedAccessException();
}
