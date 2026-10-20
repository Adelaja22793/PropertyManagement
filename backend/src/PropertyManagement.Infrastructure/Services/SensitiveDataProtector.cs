using Microsoft.AspNetCore.DataProtection;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>Encrypts identity numbers with ASP.NET Core Data Protection under a purpose of their own.</summary>
public sealed class SensitiveDataProtector(IDataProtectionProvider provider) : ISensitiveDataProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("PropertyManagement.TenantIdentity.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
