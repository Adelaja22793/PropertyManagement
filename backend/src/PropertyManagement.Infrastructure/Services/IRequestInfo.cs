namespace PropertyManagement.Infrastructure.Services;

/// <summary>Details of the current HTTP request needed for audit records.</summary>
public interface IRequestInfo
{
    string? CorrelationId { get; }
    string? IpAddress { get; }
}

public sealed class NoRequestInfo : IRequestInfo
{
    public string? CorrelationId => null;
    public string? IpAddress => null;
}
