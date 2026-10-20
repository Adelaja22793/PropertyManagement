using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

public sealed class AuditLogger(
    AppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IRequestInfo request,
    IServiceScopeFactory scopes) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string? resourceType = null, string? resourceId = null,
        AuditOutcome outcome = AuditOutcome.Success, object? details = null, Guid? organizationId = null, Guid? userId = null) =>
        db.AuditLogs.Add(Build(action, resourceType, resourceId, outcome, details, organizationId, userId));

    public async Task RecordNowAsync(string action, string? resourceType = null, string? resourceId = null,
        AuditOutcome outcome = AuditOutcome.Success, object? details = null, Guid? organizationId = null, Guid? userId = null,
        CancellationToken ct = default)
    {
        // A separate context, so that an unrelated pending change is never saved by accident.
        var log = Build(action, resourceType, resourceId, outcome, details, organizationId, userId);
        await using var scope = scopes.CreateAsyncScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        auditDb.AuditLogs.Add(log);
        await auditDb.SaveChangesAsync(ct);
    }

    private AuditLog Build(string action, string? resourceType, string? resourceId, AuditOutcome outcome,
        object? details, Guid? organizationId, Guid? userId) =>
        new(organizationId ?? currentOrganization.OrganizationId, userId ?? currentUser.UserId, action, resourceType,
            resourceId, outcome, request.CorrelationId, request.IpAddress,
            details is null ? null : JsonSerializer.Serialize(details, Json));
}
