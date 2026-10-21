using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Leases;

/// <summary>
/// An organization's lease reminder settings (design 6.5 and 8.9): how many days before a lease ends staff are
/// reminded, and whether tenants are reminded too. An organization with no row uses <see cref="Defaults"/>.
/// </summary>
public class LeaseSettings : IOrganizationScoped
{
    public const int MaxReminders = 5;
    public const int MaxReminderDays = 365;
    public static readonly int[] DefaultStaffDays = [90, 60, 30];
    public static readonly int[] DefaultTenantDays = [60, 30];

    private LeaseSettings() { }

    public LeaseSettings(Guid organizationId)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        StaffReminderDays = Format(DefaultStaffDays);
        TenantReminderDays = Format(DefaultTenantDays);
    }

    /// <summary>The settings an organization has until it saves its own. Not saved.</summary>
    public static LeaseSettings Defaults(Guid organizationId) => new(organizationId);

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    /// <summary>Days before a lease ends that staff are emailed, largest first, such as "90,60,30".</summary>
    public string StaffReminderDays { get; private set; } = "";
    public bool TenantRemindersEnabled { get; private set; }
    /// <summary>Days before a lease ends that its tenants are emailed, when tenant reminders are on.</summary>
    public string TenantReminderDays { get; private set; } = "";
    public DateTime? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public int[] GetStaffDays() => Parse(StaffReminderDays);
    public int[] GetTenantDays() => Parse(TenantReminderDays);

    public void Update(IEnumerable<int> staffDays, bool tenantRemindersEnabled, IEnumerable<int> tenantDays, DateTime nowUtc)
    {
        StaffReminderDays = Format(Validate(staffDays, "staff"));
        TenantRemindersEnabled = tenantRemindersEnabled;
        var tenant = tenantDays.ToArray();
        // Tenant days are kept while reminders are off, so turning them back on restores the choice.
        TenantReminderDays = tenant.Length == 0 && !tenantRemindersEnabled
            ? Format(DefaultTenantDays)
            : Format(Validate(tenant, "tenant"));
        UpdatedAtUtc = nowUtc;
    }

    private static int[] Validate(IEnumerable<int> days, string who)
    {
        var list = days.Distinct().OrderDescending().ToArray();
        if (list.Length == 0) throw new DomainException($"Choose at least one day for {who} reminders.");
        if (list.Length > MaxReminders) throw new DomainException($"Choose at most {MaxReminders} days for {who} reminders.");
        if (list.Any(d => d is < 1 or > MaxReminderDays))
            throw new DomainException($"Reminder days must be between 1 and {MaxReminderDays}.");
        return list;
    }

    private static string Format(IEnumerable<int> days) => string.Join(',', days);

    private static int[] Parse(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(int.Parse).OrderDescending().ToArray();
}
