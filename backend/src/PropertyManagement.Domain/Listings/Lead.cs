using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Listings;

public enum LeadStatus { New, Contacted, ViewingBooked, Applied, Won, Lost }

public enum LeadSource { Website, Phone, WalkIn, Referral, Other }

public enum ViewingStatus { Scheduled, Completed, NoShow, Cancelled }

/// <summary>Someone interested in a listing (design 6.12): from the public enquiry form or added by staff.</summary>
public class Lead : IOrganizationScoped
{
    private readonly List<Viewing> _viewings = [];

    private Lead() { }

    public Lead(Guid organizationId, Guid listingId, int number, string name, string? phone, string? email, string? message,
        LeadSource source, Guid? createdByUserId, DateTime nowUtc)
    {
        Guard.Defined(source, "Source");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        ListingId = listingId;
        Number = number;
        Name = Guard.Required(name, "Name", 200);
        Phone = Guard.Optional(phone, "Phone", 30);
        Email = Guard.Optional(email, "Email", 256);
        if (Email is not null && !Email.Contains('@')) throw new DomainException("Email is not valid.");
        if (Phone is null && Email is null) throw new DomainException("Give a phone number or email so we can reply.");
        Message = Guard.Optional(message, "Message", 2000);
        Source = source;
        Status = LeadStatus.New;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ListingId { get; private set; }
    public int Number { get; private set; }
    public string Reference => $"ENQ-{Number:D5}";
    public string Name { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Message { get; private set; }
    public LeadSource Source { get; private set; }
    public LeadStatus Status { get; private set; }
    /// <summary>Staff notes on the conversation. Never shown publicly.</summary>
    public string? Notes { get; private set; }
    /// <summary>Null when the enquiry came from the public site.</summary>
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<Viewing> Viewings => _viewings;

    public bool IsOpen => Status is not (LeadStatus.Won or LeadStatus.Lost);

    public void SetStatus(LeadStatus status, string? notes, DateTime nowUtc)
    {
        Guard.Defined(status, "Status");
        Status = status;
        if (notes is not null) Notes = Guard.Optional(notes, "Notes", 4000);
        UpdatedAtUtc = nowUtc;
    }

    public Viewing ScheduleViewing(DateTime scheduledAtUtc, Guid? agentUserId, string? notes, DateTime nowUtc)
    {
        if (!IsOpen) throw new DomainException("This enquiry is closed. Reopen it to book a viewing.", "lead_closed");
        if (scheduledAtUtc < nowUtc.AddHours(-1)) throw new DomainException("A viewing can't be booked in the past.");
        var viewing = new Viewing(OrganizationId, Id, scheduledAtUtc, agentUserId, notes, nowUtc);
        _viewings.Add(viewing);
        if (Status is LeadStatus.New or LeadStatus.Contacted) Status = LeadStatus.ViewingBooked;
        UpdatedAtUtc = nowUtc;
        return viewing;
    }

    public Viewing RecordViewing(Guid viewingId, ViewingStatus status, string? outcome, DateTime nowUtc)
    {
        var viewing = _viewings.SingleOrDefault(v => v.Id == viewingId) ?? throw new DomainException("Viewing not found.");
        viewing.Record(status, outcome, nowUtc);
        UpdatedAtUtc = nowUtc;
        return viewing;
    }
}

public class Viewing : IOrganizationScoped
{
    private Viewing() { }

    internal Viewing(Guid organizationId, Guid leadId, DateTime scheduledAtUtc, Guid? agentUserId, string? notes, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeadId = leadId;
        ScheduledAtUtc = scheduledAtUtc;
        AgentUserId = agentUserId;
        Notes = Guard.Optional(notes, "Notes", 1000);
        Status = ViewingStatus.Scheduled;
        CreatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeadId { get; private set; }
    public DateTime ScheduledAtUtc { get; private set; }
    public Guid? AgentUserId { get; private set; }
    public string? Notes { get; private set; }
    public ViewingStatus Status { get; private set; }
    public string? Outcome { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RecordedAtUtc { get; private set; }

    internal void Record(ViewingStatus status, string? outcome, DateTime nowUtc)
    {
        Guard.Defined(status, "Status");
        if (status == ViewingStatus.Scheduled) throw new DomainException("Choose how the viewing went.");
        if (Status != ViewingStatus.Scheduled) throw new DomainException("This viewing's outcome is already recorded.", "already_decided");
        if (status is ViewingStatus.Completed or ViewingStatus.NoShow && ScheduledAtUtc > nowUtc.AddHours(1))
            throw new DomainException("This viewing hasn't happened yet. Cancel it instead.");
        Status = status;
        Outcome = Guard.Optional(outcome, "Outcome", 1000);
        RecordedAtUtc = nowUtc;
    }
}
