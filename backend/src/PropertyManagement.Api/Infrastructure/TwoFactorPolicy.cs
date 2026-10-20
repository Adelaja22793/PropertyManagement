namespace PropertyManagement.Api.Infrastructure;

/// <summary>
/// Two-step sign-in is required for admins, accountants and finance approvers (design 11.1). They get a short grace
/// period from when the role is given (<c>Security:TwoFactorGraceDays</c>, 7 by default) to turn it on; after that
/// the organization's data is closed to them until they do.
/// </summary>
public sealed class TwoFactorPolicy(IConfiguration configuration)
{
    public int GraceDays => Math.Max(0, configuration.GetValue("Security:TwoFactorGraceDays", 7));

    public bool IsOverdue(DateTime? requiredSinceUtc, DateTime nowUtc) =>
        requiredSinceUtc is { } since && nowUtc > since.AddDays(GraceDays);
}
