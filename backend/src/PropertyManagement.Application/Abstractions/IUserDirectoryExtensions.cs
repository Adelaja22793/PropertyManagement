namespace PropertyManagement.Application.Abstractions;

public interface IUserLookup
{
    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct);
}
