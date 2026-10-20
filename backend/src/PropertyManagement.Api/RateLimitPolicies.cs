namespace PropertyManagement.Api;

public static class RateLimitPolicies
{
    /// <summary>Sign-in, registration, password recovery and invitation acceptance (design 11.1).</summary>
    public const string Auth = "auth";

    /// <summary>Public listing pages and photos, per client address.</summary>
    public const string Public = "public";

    /// <summary>Public enquiry form, per client address: a few a minute, to keep spam down.</summary>
    public const string Enquiry = "enquiry";
}
