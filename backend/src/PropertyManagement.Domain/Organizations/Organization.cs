using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Organizations;

public enum OrganizationType
{
    ManagementCompany = 0,
    IndividualLandlord = 1,
}

public enum OrganizationStatus
{
    Active = 0,
    Suspended = 1,
    Cancelled = 2,
}

/// <summary>A property management company or self-managing landlord: the SaaS tenant.</summary>
public class Organization
{
    private Organization() { }

    public Organization(string name, string slug, OrganizationType type)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Organization name is required.");
        Id = Guid.NewGuid();
        Name = name.Trim();
        Slug = slug;
        Type = type;
        Status = OrganizationStatus.Active;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public OrganizationType Type { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public string BaseCurrency { get; private set; } = "NGN";
    public string TimeZone { get; private set; } = "Africa/Lagos";
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? Address { get; private set; }
    public string? TaxIdentificationNumber { get; private set; }
    public string? VatNumber { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void UpdateProfile(string name, string? contactEmail, string? contactPhone, string? address,
        string? tin, string? vatNumber, string timeZone)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Organization name is required.");
        Name = name.Trim();
        ContactEmail = contactEmail?.Trim();
        ContactPhone = contactPhone?.Trim();
        Address = address?.Trim();
        TaxIdentificationNumber = tin?.Trim();
        VatNumber = vatNumber?.Trim();
        TimeZone = timeZone;
    }

    public static string CreateSlug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return string.IsNullOrEmpty(slug) ? "org" : slug;
    }
}
