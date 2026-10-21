using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Domain.Platform;

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Cancelled = 4,
}

/// <summary>
/// An organization's plan and where it is in the lifecycle of design 4.8: trial or active, then past due with a grace
/// period when the paid-up date passes, then suspended (read-only), and cancelled with a window to export everything.
/// Taking payment is not part of this; platform staff record the paid-up date.
/// </summary>
public class OrganizationSubscription : IOrganizationScoped
{
    /// <summary>Days of full access, with a banner and reminders, after the trial or paid period ends.</summary>
    public const int GraceDays = 14;
    /// <summary>Days after cancelling during which admins can still download all data.</summary>
    public const int ExportDays = 90;

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
    public DateTime? PastDueSinceUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    /// <summary>The last lifecycle email sent, so each one goes out once.</summary>
    public string? LastNoticeKey { get; private set; }

    /// <summary>When the company becomes read-only if nothing changes; set while past due.</summary>
    public DateTime? SuspendsAtUtc => Status == SubscriptionStatus.PastDue ? PastDueSinceUtc?.AddDays(GraceDays) : null;

    /// <summary>Until when admins can download the company's data; set once cancelled.</summary>
    public DateTime? ExportUntilUtc => Status == SubscriptionStatus.Cancelled ? CancelledAtUtc?.AddDays(ExportDays) : null;

    /// <summary>What the organization's status should be at this point in the lifecycle.</summary>
    public OrganizationStatus OrganizationStatus => Status switch
    {
        SubscriptionStatus.Suspended => OrganizationStatus.Suspended,
        SubscriptionStatus.Cancelled => OrganizationStatus.Cancelled,
        _ => OrganizationStatus.Active,
    };

    /// <summary>Moves on one step if a date has passed. Returns false when nothing changed.</summary>
    public bool Advance(DateTime nowUtc)
    {
        switch (Status)
        {
            case SubscriptionStatus.Trial when TrialEndsAtUtc <= nowUtc:
                Status = SubscriptionStatus.PastDue;
                PastDueSinceUtc = TrialEndsAtUtc;
                return true;
            case SubscriptionStatus.Active when CurrentPeriodEndsAtUtc <= nowUtc:
                Status = SubscriptionStatus.PastDue;
                PastDueSinceUtc = CurrentPeriodEndsAtUtc;
                return true;
            case SubscriptionStatus.PastDue when SuspendsAtUtc <= nowUtc:
                Status = SubscriptionStatus.Suspended;
                SuspendedAtUtc = nowUtc;
                return true;
            default:
                return false;
        }
    }

    public void ExtendTrial(DateTime untilUtc, DateTime nowUtc)
    {
        RequireNotCancelled();
        if (untilUtc <= nowUtc) throw new DomainException("Choose a date in the future.", "date_in_past");
        Status = SubscriptionStatus.Trial;
        TrialEndsAtUtc = untilUtc;
        ClearLapse();
    }

    /// <summary>Records that the organization has paid up to a date, on a plan. Also reinstates a cancelled one.</summary>
    public void Activate(SubscriptionPlan plan, DateTime untilUtc, DateTime nowUtc)
    {
        if (untilUtc <= nowUtc) throw new DomainException("Choose a date in the future.", "date_in_past");
        PlanId = plan.Id;
        Status = SubscriptionStatus.Active;
        CurrentPeriodEndsAtUtc = untilUtc;
        CancelledAtUtc = null;
        ClearLapse();
    }

    public void ChangePlan(SubscriptionPlan plan)
    {
        RequireNotCancelled();
        PlanId = plan.Id;
    }

    public void Suspend(DateTime nowUtc)
    {
        RequireNotCancelled();
        if (Status == SubscriptionStatus.Suspended) return;
        Status = SubscriptionStatus.Suspended;
        SuspendedAtUtc = nowUtc;
    }

    public void Cancel(DateTime nowUtc)
    {
        if (Status == SubscriptionStatus.Cancelled) return;
        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = nowUtc;
    }

    /// <summary>True the first time a notice with this key is asked for; false after that.</summary>
    public bool TakeNotice(string key)
    {
        if (LastNoticeKey == key) return false;
        LastNoticeKey = key;
        return true;
    }

    private void ClearLapse()
    {
        PastDueSinceUtc = null;
        SuspendedAtUtc = null;
    }

    private void RequireNotCancelled()
    {
        if (Status == SubscriptionStatus.Cancelled)
            throw new DomainException("This subscription is cancelled. Reinstate it with a paid-up date first.", "subscription_cancelled");
    }
}
