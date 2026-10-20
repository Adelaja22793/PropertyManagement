using PropertyManagement.Domain.Auditing;

namespace PropertyManagement.Application.Abstractions;

public interface IAuditLogger
{
    /// <summary>Adds an audit record to the current unit of work. It is saved with the change it describes.</summary>
    void Record(string action, string? resourceType = null, string? resourceId = null,
        AuditOutcome outcome = AuditOutcome.Success, object? details = null, Guid? organizationId = null, Guid? userId = null);

    /// <summary>Saves an audit record immediately, for events with no other change (failed logins, denials).</summary>
    Task RecordNowAsync(string action, string? resourceType = null, string? resourceId = null,
        AuditOutcome outcome = AuditOutcome.Success, object? details = null, Guid? organizationId = null, Guid? userId = null,
        CancellationToken ct = default);
}
