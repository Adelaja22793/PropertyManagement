namespace PropertyManagement.Domain.Common;

/// <summary>Light normalisation for contact details. It catches typos, not every invalid address.</summary>
public static class Contact
{
    public static string? Email(string? value)
    {
        var email = Guard.Optional(value, "Email", 256)?.ToLowerInvariant();
        if (email is null) return null;
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || !email[(at + 1)..].Contains('.') || email.Contains(' '))
            throw new DomainException("Enter a valid email address.", "invalid_email");
        return email;
    }

    public static string? Phone(string? value, string label = "Phone")
    {
        var phone = Guard.Optional(value, label, 30);
        if (phone is null) return null;
        var digits = phone.Count(char.IsAsciiDigit);
        if (digits < 7 || phone.Any(c => !(char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')')))
            throw new DomainException($"{label} is not a valid phone number.", "invalid_phone");
        return phone;
    }
}
