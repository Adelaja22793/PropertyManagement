using Microsoft.AspNetCore.Identity;

namespace PropertyManagement.Infrastructure.Identity;

/// <summary>A login identity. One person has one user, and a membership per organization they work with.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public bool IsPlatformAdmin { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
