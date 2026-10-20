using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Organizations;

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Cancelled = 2,
}

/// <summary>An invitation for someone to join an organization. Only a hash of the token is stored.</summary>
public class UserInvitation : IOrganizationScoped
{
    private UserInvitation() { }

    public UserInvitation(Guid organizationId, string email, string roleName, IEnumerable<Guid> branchIds,
        string tokenHash, Guid invitedByUserId, TimeSpan validFor)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Email = email.Trim().ToLowerInvariant();
        RoleName = roleName;
        BranchIds = string.Join(',', branchIds.Distinct());
        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        Status = InvitationStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = CreatedAtUtc.Add(validFor);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Email { get; private set; } = "";
    public string RoleName { get; private set; } = "";
    /// <summary>Comma-separated branch IDs to scope the new member to; empty for the whole organization.</summary>
    public string BranchIds { get; private set; } = "";
    public string TokenHash { get; private set; } = "";
    public Guid InvitedByUserId { get; private set; }
    public InvitationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }

    public IEnumerable<Guid> GetBranchIds() =>
        BranchIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse);

    public bool IsUsable(DateTime nowUtc) => Status == InvitationStatus.Pending && nowUtc < ExpiresAtUtc;

    public void Accept(Guid userId, string userEmail, DateTime nowUtc)
    {
        if (!IsUsable(nowUtc)) throw new DomainException("This invitation is no longer valid.", "invitation_invalid");
        if (!string.Equals(Email, userEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("This invitation was sent to a different email address.", "invitation_email_mismatch");
        Status = InvitationStatus.Accepted;
        AcceptedAtUtc = nowUtc;
        AcceptedByUserId = userId;
    }

    public void Cancel()
    {
        if (Status == InvitationStatus.Pending) Status = InvitationStatus.Cancelled;
    }
}
