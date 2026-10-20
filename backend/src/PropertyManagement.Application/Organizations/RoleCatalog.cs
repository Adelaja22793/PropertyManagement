using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Organizations;

/// <summary>Resolves role keys, built-in or custom, to permissions for the active organization.</summary>
public sealed class RoleCatalog(IAppDbContext db)
{
    private IReadOnlyDictionary<string, IReadOnlySet<string>>? _custom;

    /// <summary>Permissions of every custom role in the active organization, by key. Loaded once per request.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> CustomAsync(CancellationToken ct) =>
        _custom ??= (await db.CustomRoles.AsNoTracking().ToListAsync(ct)).ToDictionary(r => r.Key, r => r.GetPermissions());

    public async Task<IReadOnlySet<string>> PermissionsForAsync(IEnumerable<string> roles, CancellationToken ct) =>
        Roles.PermissionsFor(roles, await CustomAsync(ct));

    /// <summary>Says whether a custom role needs two-step sign-in, for <see cref="OrganizationMembership.SetRoles"/>.</summary>
    public async Task<Func<string, bool>> NeedsTwoFactorAsync(CancellationToken ct)
    {
        var custom = await CustomAsync(ct);
        return key => custom.TryGetValue(key, out var granted) && Roles.NeedsTwoFactor(granted);
    }

    public void Forget() => _custom = null;

    /// <summary>
    /// Loads the custom roles among <paramref name="roles"/> from any organization, for code that runs before an
    /// organization is selected (the request middleware, the organization switcher, accepting an invitation).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> LoadAsync(IQueryable<CustomRole> customRoles,
        IEnumerable<string> roles, CancellationToken ct)
    {
        var ids = roles.Select(CustomRole.IdFromKey).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, IReadOnlySet<string>>();
        var rows = await customRoles.IgnoreQueryFilters().AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => r.GetPermissions());
    }
}
