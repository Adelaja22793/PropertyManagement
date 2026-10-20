namespace PropertyManagement.Application.Abstractions;

public sealed record UserSummary(Guid Id, string Email, string FullName);

/// <summary>Read access to login identities, which live outside any one organization.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IEnumerable<Guid> userIds, CancellationToken ct);
}
