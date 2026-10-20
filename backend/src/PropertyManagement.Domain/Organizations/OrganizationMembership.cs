using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Organizations;

public enum MembershipStatus
{
    Active = 0,
    Revoked = 1,
}

/// <summary>Links a login user to an organization, with roles and optional branch scope.</summary>
public class OrganizationMembership : IOrganizationScoped
{
    private readonly List<MembershipRole> _roles = [];
    private readonly List<MembershipBranchScope> _branchScopes = [];

    private OrganizationMembership() { }

    public OrganizationMembership(Guid organizationId, Guid userId, IEnumerable<string> roles)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        UserId = userId;
        Status = MembershipStatus.Active;
        CreatedAtUtc = DateTime.UtcNow;
        SetRoles(roles);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<MembershipRole> Roles => _roles;
    public IReadOnlyCollection<MembershipBranchScope> BranchScopes => _branchScopes;

    public bool IsActive => Status == MembershipStatus.Active;

    public void SetRoles(IEnumerable<string> roles)
    {
        var distinct = roles.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToList();
        if (distinct.Count == 0) throw new DomainException("A member needs at least one role.");
        _roles.RemoveAll(r => !distinct.Contains(r.RoleName));
        foreach (var role in distinct.Where(r => _roles.All(x => x.RoleName != r)))
            _roles.Add(new MembershipRole(OrganizationId, Id, role));
    }

    public void SetBranchScope(IEnumerable<Guid> branchIds)
    {
        var ids = branchIds.Distinct().ToList();
        _branchScopes.RemoveAll(s => !ids.Contains(s.BranchId));
        foreach (var id in ids.Where(id => _branchScopes.All(s => s.BranchId != id)))
            _branchScopes.Add(new MembershipBranchScope(OrganizationId, Id, id));
    }

    public void Revoke()
    {
        if (Status == MembershipStatus.Revoked) return;
        Status = MembershipStatus.Revoked;
        RevokedAtUtc = DateTime.UtcNow;
    }
}

public class MembershipRole : IOrganizationScoped
{
    private MembershipRole() { }

    internal MembershipRole(Guid organizationId, Guid membershipId, string roleName)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        MembershipId = membershipId;
        RoleName = roleName;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid MembershipId { get; private set; }
    public string RoleName { get; private set; } = "";
}

/// <summary>Limits a membership to specific branches. No rows means the whole organization.</summary>
public class MembershipBranchScope : IOrganizationScoped
{
    private MembershipBranchScope() { }

    internal MembershipBranchScope(Guid organizationId, Guid membershipId, Guid branchId)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        MembershipId = membershipId;
        BranchId = branchId;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid MembershipId { get; private set; }
    public Guid BranchId { get; private set; }
}
