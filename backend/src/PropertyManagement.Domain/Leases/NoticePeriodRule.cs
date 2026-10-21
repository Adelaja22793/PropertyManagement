using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Domain.Leases;

public sealed record NoticePeriodRuleDetails(string? State, NoticeType Type, BillingFrequency? Frequency, int Days, string? Basis);

/// <summary>
/// An organization's own notice period (design 8.9), for one notice type and optionally one state and rent
/// frequency. It replaces the built-in Lagos default when it matches a lease. The organization's lawyer decides the
/// numbers; the product never assumes one is lawful.
/// </summary>
public class NoticePeriodRule : IOrganizationScoped
{
    public const int MaxDays = 730;

    private NoticePeriodRule() { }

    public NoticePeriodRule(Guid organizationId, NoticePeriodRuleDetails details, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Apply(details);
        CreatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    /// <summary>The Nigerian state it applies to, or null for every state.</summary>
    public string? State { get; private set; }
    public NoticeType Type { get; private set; }
    /// <summary>The rent frequency it applies to, or null for every frequency.</summary>
    public BillingFrequency? Frequency { get; private set; }
    public int Days { get; private set; }
    /// <summary>Where the number comes from, such as a section of a state's tenancy law or the lawyer's advice.</summary>
    public string? Basis { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(NoticePeriodRuleDetails details, DateTime nowUtc)
    {
        Apply(details);
        UpdatedAtUtc = nowUtc;
    }

    public bool Matches(NoticeType type, string? state, BillingFrequency frequency) =>
        Type == type
        && (State is null || string.Equals(State, state?.Trim(), StringComparison.OrdinalIgnoreCase))
        && (Frequency is null || Frequency == frequency);

    /// <summary>Higher is more specific: a state match counts for more than a frequency match.</summary>
    public int Specificity => (State is null ? 0 : 2) + (Frequency is null ? 0 : 1);

    private void Apply(NoticePeriodRuleDetails d)
    {
        Guard.Defined(d.Type, "Notice type");
        if (d.Frequency is { } frequency) Guard.Defined(frequency, "Rent frequency");
        if (d.Days is < 1 or > MaxDays) throw new DomainException($"The notice period must be between 1 and {MaxDays} days.");
        State = Guard.Optional(d.State, "State", 50);
        Type = d.Type;
        Frequency = d.Frequency;
        Days = d.Days;
        Basis = Guard.Optional(d.Basis, "Legal basis", 300);
    }
}
