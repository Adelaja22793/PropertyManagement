namespace PropertyManagement.Domain.Auditing;

public enum AuditOutcome
{
    Success = 0,
    Denied = 1,
    Failed = 2,
}

/// <summary>
/// Append-only record of a security event or sensitive action. OrganizationId is null for
/// events that happen before an organization is known, such as a failed login.
/// </summary>
public class AuditLog
{
    private AuditLog() { }

    public AuditLog(Guid? organizationId, Guid? userId, string action, string? resourceType, string? resourceId,
        AuditOutcome outcome, string? correlationId, string? ipAddress, string? details)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        UserId = userId;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Outcome = outcome;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
        Details = details;
        TimestampUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = "";
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? Details { get; private set; }
    public DateTime TimestampUtc { get; private set; }
}
