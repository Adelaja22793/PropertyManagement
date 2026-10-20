using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Platform;

public enum SupportGrantStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2,
    Revoked = 3,
}

/// <summary>
/// Read-only access for a platform support person to one organization, approved by its admin (design 4.7).
/// Platform staff have no standing access; each grant lasts at most a day and can be revoked at any time.
/// It is a platform record rather than an organization-scoped one because the support person, who belongs to no
/// organization, creates it; every query names the organization explicitly.
/// </summary>
public class SupportGrant
{
    public const int MaxHours = 24;
    public const int MaxReasonLength = 500;

    private SupportGrant() { }

    public SupportGrant(Guid organizationId, Guid requestedByUserId, string reason, int hours, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Say why you need access.", "reason_required");
        if (reason.Trim().Length > MaxReasonLength) throw new DomainException($"Keep the reason under {MaxReasonLength} characters.", "reason_too_long");
        if (hours is < 1 or > MaxHours) throw new DomainException($"Access can last from 1 to {MaxHours} hours.", "invalid_hours");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        RequestedByUserId = requestedByUserId;
        Reason = reason.Trim();
        Hours = hours;
        Status = SupportGrantStatus.Pending;
        RequestedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string Reason { get; private set; } = "";
    public int Hours { get; private set; }
    public SupportGrantStatus Status { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive(DateTime nowUtc) => Status == SupportGrantStatus.Approved && ExpiresAtUtc > nowUtc;

    /// <summary>Starts the access now, for the hours asked for.</summary>
    public void Approve(Guid userId, DateTime nowUtc)
    {
        RequirePending();
        Status = SupportGrantStatus.Approved;
        DecidedByUserId = userId;
        DecidedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.AddHours(Hours);
    }

    public void Decline(Guid userId, DateTime nowUtc)
    {
        RequirePending();
        Status = SupportGrantStatus.Declined;
        DecidedByUserId = userId;
        DecidedAtUtc = nowUtc;
    }

    /// <summary>Ends access at once. Also withdraws a request that hasn't been answered.</summary>
    public void Revoke(Guid userId, DateTime nowUtc)
    {
        if (Status == SupportGrantStatus.Pending)
        {
            Status = SupportGrantStatus.Revoked;
            RevokedByUserId = userId;
            RevokedAtUtc = nowUtc;
            return;
        }
        if (!IsActive(nowUtc)) throw new DomainException("This access has already ended.", "already_decided");
        Status = SupportGrantStatus.Revoked;
        RevokedByUserId = userId;
        RevokedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc;
    }

    private void RequirePending()
    {
        if (Status != SupportGrantStatus.Pending) throw new DomainException("This request has already been answered.", "already_decided");
    }
}
