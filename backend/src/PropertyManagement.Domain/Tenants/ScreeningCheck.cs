using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Tenants;

public enum ScreeningCheckType { Employer = 0, PreviousLandlord = 1, PersonalReference = 2, Identity = 3, Other = 4 }

public enum ScreeningOutcome { Pending = 0, Passed = 1, Concern = 2, Failed = 3 }

/// <summary>A reference or background check on a tenant, with who did it (design 6.4). Kept as history.</summary>
public class ScreeningCheck : IOrganizationScoped
{
    private ScreeningCheck() { }

    public ScreeningCheck(Guid organizationId, Guid tenantId, ScreeningCheckType type, ScreeningOutcome outcome,
        string? notes, Guid checkedByUserId, DateTime nowUtc)
    {
        Guard.Defined(type, "Check type");
        Guard.Defined(outcome, "Outcome");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        TenantId = tenantId;
        Type = type;
        Outcome = outcome;
        Notes = Guard.Optional(notes, "Notes", 2000);
        CheckedByUserId = checkedByUserId;
        CheckedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid TenantId { get; private set; }
    public ScreeningCheckType Type { get; private set; }
    public ScreeningOutcome Outcome { get; private set; }
    public string? Notes { get; private set; }
    public Guid CheckedByUserId { get; private set; }
    public DateTime CheckedAtUtc { get; private set; }
}
