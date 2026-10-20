namespace PropertyManagement.Api;

public static class RateLimitPolicies
{
    /// <summary>Sign-in, registration, password recovery and invitation acceptance (design 11.1).</summary>
    public const string Auth = "auth";
}
