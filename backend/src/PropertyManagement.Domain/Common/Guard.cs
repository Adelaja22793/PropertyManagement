namespace PropertyManagement.Domain.Common;

/// <summary>Small checks shared by entities. Each failure is a <see cref="DomainException"/>.</summary>
public static class Guard
{
    public static string Required(string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{label} is required.");
        return Optional(value, label, maxLength)!;
    }

    public static string? Optional(string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) throw new DomainException($"{label} must be {maxLength} characters or fewer.");
        return trimmed;
    }

    public static decimal NonNegative(decimal value, string label) =>
        value < 0 ? throw new DomainException($"{label} cannot be negative.") : value;

    public static int? NonNegative(int? value, string label) =>
        value < 0 ? throw new DomainException($"{label} cannot be negative.") : value;

    public static decimal? NonNegative(decimal? value, string label) =>
        value < 0 ? throw new DomainException($"{label} cannot be negative.") : value;

    public static string Currency(string? value)
    {
        var code = (value ?? "NGN").Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new DomainException("Currency must be a three-letter ISO code such as NGN.");
        return code;
    }

    public static void Defined<TEnum>(TEnum value, string label) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new DomainException($"{label} is not valid.");
    }
}
