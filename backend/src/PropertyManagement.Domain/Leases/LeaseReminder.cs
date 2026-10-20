using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Leases;

/// <summary>
/// Records that a reminder about a lease was sent, so each one goes out once however often the job runs.
/// <see cref="Key"/> names the reminder, such as "expiry-90" or "notice-expired-{noticeId}".
/// </summary>
public class LeaseReminder : IOrganizationScoped
{
    private LeaseReminder() { }

    public LeaseReminder(Guid organizationId, Guid leaseId, string key, int recipientCount, DateTime sentAtUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeaseId = leaseId;
        Key = Guard.Required(key, "Reminder key", 100);
        RecipientCount = recipientCount;
        SentAtUtc = sentAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeaseId { get; private set; }
    public string Key { get; private set; } = "";
    public int RecipientCount { get; private set; }
    public DateTime SentAtUtc { get; private set; }
}
