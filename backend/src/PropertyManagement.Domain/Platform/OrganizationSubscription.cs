using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Platform;

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Cancelled = 4,
}

public class OrganizationSubscription : IOrganizationScoped
{
    private OrganizationSubscription() { }

    public static OrganizationSubscription StartTrial(Guid organizationId, SubscriptionPlan plan, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        PlanId = plan.Id,
        Status = SubscriptionStatus.Trial,
        StartedAtUtc = nowUtc,
        TrialEndsAtUtc = nowUtc.AddDays(plan.TrialDays),
    };

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? TrialEndsAtUtc { get; private set; }
    public DateTime? CurrentPeriodEndsAtUtc { get; private set; }
}
