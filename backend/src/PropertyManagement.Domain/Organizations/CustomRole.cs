using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Organizations;

/// <summary>
/// A role an organization defines itself from the permission list (design 5). Members hold it by its <see cref="Key"/>,
/// so it can be renamed without touching their memberships.
/// </summary>
public class CustomRole : IOrganizationScoped
{
    /// <summary>Custom role keys start with this, so they can never clash with a built-in role name.</summary>
    public const string KeyPrefix = "custom:";

    private CustomRole() { }

    public CustomRole(Guid organizationId, string name, string? description, IEnumerable<string> permissions)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(name, description, permissions);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    /// <summary>Permission codes, comma-separated and sorted.</summary>
    public string Permissions { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public string Key => KeyFor(Id);

    public IReadOnlySet<string> GetPermissions() =>
        Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        Apply(name, description, permissions);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void Apply(string name, string? description, IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Give the role a name.", "role_name_required");
        if (name.Trim().Length > 100) throw new DomainException("Keep the role name to 100 characters.", "role_name_too_long");
        if (description?.Trim().Length > 500) throw new DomainException("Keep the description to 500 characters.", "role_description_too_long");
        var codes = permissions.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        if (codes.Count == 0) throw new DomainException("Choose at least one permission.", "role_permissions_required");
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Permissions = string.Join(',', codes);
    }

    public static string KeyFor(Guid id) => KeyPrefix + id.ToString("D");

    public static bool IsCustomKey(string role) => role.StartsWith(KeyPrefix, StringComparison.Ordinal);

    public static Guid? IdFromKey(string role) =>
        IsCustomKey(role) && Guid.TryParse(role.AsSpan(KeyPrefix.Length), out var id) ? id : null;
}
