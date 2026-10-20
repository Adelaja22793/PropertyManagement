namespace PropertyManagement.Domain.Common;

/// <summary>A business rule was broken. Maps to HTTP 400 or 409.</summary>
public class DomainException(string message, string code = "domain_rule") : Exception(message)
{
    public string Code { get; } = code;
}
