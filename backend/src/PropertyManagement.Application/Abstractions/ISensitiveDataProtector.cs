namespace PropertyManagement.Application.Abstractions;

/// <summary>
/// Encrypts identity numbers (NIN, BVN) before they are stored, so a database copy alone does not reveal them
/// (design 6.4 and 11.4). Implemented with ASP.NET Core Data Protection.
/// </summary>
public interface ISensitiveDataProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
