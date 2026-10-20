using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Organizations;

public sealed record RoleDto(string Key, Guid? Id, string Name, string? Description, bool IsCustom, IReadOnlyList<string> Permissions,
    bool RequiresTwoFactor, int MemberCount);

public sealed record PermissionGroupDto(string Group, IReadOnlyList<string> Permissions);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

/// <summary>
/// Roles an organization defines from the permission list (design 5). Nobody can give a role a permission they don't
/// hold themselves, and a role with admin or finance powers needs two-step sign-in like the built-in ones.
/// </summary>
public sealed class CustomRoleService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    RoleCatalog catalog,
    IAuditLogger audit,
    IClock clock)
{
    public static IReadOnlyList<PermissionGroupDto> PermissionGroups { get; } =
        Permissions.Groups.Select(g => new PermissionGroupDto(g.Group, g.Codes)).ToList();

    /// <summary>Built-in staff roles first, in the design's order, then the organization's own by name.</summary>
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct)
    {
        var counts = await MemberCountsAsync(ct);
        var builtIn = Roles.StaffRoleNames.Select(r =>
        {
            var granted = Roles.BuiltInPermissions(r.Key);
            return new RoleDto(r.Key, null, r.Name, r.Description, false, granted.Order(StringComparer.Ordinal).ToList(),
                OrganizationMembership.TwoFactorRoles.Contains(r.Key), counts.GetValueOrDefault(r.Key));
        });
        var custom = (await db.CustomRoles.OrderBy(r => r.Name).ToListAsync(ct)).Select(r => ToDto(r, counts.GetValueOrDefault(r.Key)));
        return builtIn.Concat(custom).ToList();
    }

    public async Task<RoleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var role = await LoadAsync(id, ct);
        return ToDto(role, (await MemberCountsAsync(ct)).GetValueOrDefault(role.Key));
    }

    public async Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken ct)
    {
        var permissions = RequireGrantable(request.Permissions);
        await EnsureUniqueNameAsync(request.Name, null, ct);
        var role = new CustomRole(currentOrganization.RequireOrganizationId(), request.Name, request.Description, permissions);
        db.CustomRoles.Add(role);
        audit.Record("role.created", nameof(CustomRole), role.Id.ToString(), details: new { role.Name, Permissions = permissions });
        await db.SaveChangesAsync(ct);
        return ToDto(role, 0);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken ct)
    {
        var role = await LoadAsync(id, ct);
        var permissions = RequireGrantable(request.Permissions);
        await EnsureUniqueNameAsync(request.Name, id, ct);
        var before = new { role.Name, Permissions = role.GetPermissions().Order(StringComparer.Ordinal).ToList() };
        role.Update(request.Name, request.Description, permissions);

        // Members holding the role may now need two-step sign-in, or no longer need it.
        catalog.Forget();
        var key = role.Key;
        var holders = await db.Memberships.Include(m => m.Roles)
            .Where(m => m.Status == MembershipStatus.Active && m.Roles.Any(r => r.RoleName == key)).ToListAsync(ct);
        var custom = (await catalog.CustomAsync(ct)).ToDictionary(r => r.Key, r => r.Value);
        custom[key] = role.GetPermissions();
        foreach (var m in holders)
            m.SetRoles(m.Roles.Select(r => r.RoleName).ToList(), k => custom.TryGetValue(k, out var granted) && Roles.NeedsTwoFactor(granted));

        audit.Record("role.updated", nameof(CustomRole), role.Id.ToString(),
            details: new { before, after = new { role.Name, Permissions = permissions } });
        await db.SaveChangesAsync(ct);
        return ToDto(role, holders.Count);
    }

    /// <summary>Deletes a role nobody holds and no pending invitation offers.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var role = await LoadAsync(id, ct);
        var key = role.Key;
        var now = clock.UtcNow;
        if (await db.Memberships.AnyAsync(m => m.Status == MembershipStatus.Active && m.Roles.Any(r => r.RoleName == key), ct))
            throw new DomainException("Give the members with this role another role first.", "role_in_use");
        if (await db.Invitations.AnyAsync(i => i.RoleName == key && i.Status == InvitationStatus.Pending && i.ExpiresAtUtc > now, ct))
            throw new DomainException("Cancel the pending invitations for this role first.", "role_in_use");
        db.CustomRoles.Remove(role);
        audit.Record("role.deleted", nameof(CustomRole), role.Id.ToString(), details: new { role.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Known permission codes the caller holds; refuses anything else.</summary>
    private List<string> RequireGrantable(IReadOnlyList<string>? requested)
    {
        var codes = (requested ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        var unknown = codes.Where(c => !Permissions.All.Contains(c)).ToList();
        if (unknown.Count > 0) throw new DomainException($"Unknown permission: {string.Join(", ", unknown)}.", "unknown_permission");
        if (codes.Any(c => !currentOrganization.HasPermission(c)))
            throw new ForbiddenException("You can't give a role permissions you don't have yourself.");
        return codes;
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var trimmed = (name ?? "").Trim();
        if (Roles.StaffRoleNames.Any(r => string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase) || r.Key == trimmed)
            || await db.CustomRoles.AnyAsync(r => r.Name == trimmed && r.Id != exceptId, ct))
            throw new DomainException("A role with this name already exists.", "role_name_taken");
    }

    private async Task<CustomRole> LoadAsync(Guid id, CancellationToken ct) =>
        await db.CustomRoles.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role");

    private async Task<Dictionary<string, int>> MemberCountsAsync(CancellationToken ct) =>
        (await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .SelectMany(m => m.Roles.Select(r => r.RoleName)).ToListAsync(ct))
        .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());

    private static RoleDto ToDto(CustomRole r, int members)
    {
        var granted = r.GetPermissions();
        return new RoleDto(r.Key, r.Id, r.Name, r.Description, true, granted.Order(StringComparer.Ordinal).ToList(),
            Roles.NeedsTwoFactor(granted), members);
    }
}
