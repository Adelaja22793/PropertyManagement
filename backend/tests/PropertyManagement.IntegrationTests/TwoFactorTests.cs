using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PropertyManagement.IntegrationTests;

/// <summary>Two-step sign-in with an authenticator app (design 11.1).</summary>
public sealed class TwoFactorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>The code an authenticator app would show now for this key (RFC 6238, 30-second steps).</summary>
    public static string Totp(string sharedKey, int stepOffset = 0)
    {
        var key = Base32(sharedKey.Replace(" ", ""));
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + stepOffset;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(message);
        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private static byte[] Base32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits < 8) continue;
            output.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }
        return output.ToArray();
    }

    /// <summary>Turns two-step sign-in on and returns the shared key and recovery codes.</summary>
    private static async Task<(string Key, List<string> Codes)> EnableAsync(TestClient client)
    {
        var setup = await client.PostAsync("/api/v1/auth/two-factor/setup", new { });
        var key = setup.GetProperty("sharedKey").GetString()!;
        var enabled = await client.PostAsync("/api/v1/auth/two-factor/enable", new { code = Totp(key) });
        return (key, enabled.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList());
    }

    private static async Task<HttpResponseMessage> PasswordStepAsync(TestClient client, string email) =>
        await client.Http.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestClient.Password }, TestClient.Json);

    [Fact]
    public async Task Turning_it_on_needs_a_code_from_the_app_and_gives_recovery_codes()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ikeja Homes");
        var status = await admin.GetAsync("/api/v1/auth/two-factor");
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.True(status.GetProperty("required").GetBoolean());
        var me = await admin.GetAsync("/api/v1/auth/me");
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("organizations")[0].GetProperty("twoFactorRequiredSinceUtc").ValueKind);

        var setup = await admin.PostAsync("/api/v1/auth/two-factor/setup", new { });
        var key = setup.GetProperty("sharedKey").GetString()!;
        Assert.StartsWith("otpauth://totp/Property%20Management:", setup.GetProperty("authenticatorUri").GetString());
        (await admin.Http.PostAsJsonAsync("/api/v1/auth/two-factor/enable", new { code = "000000" == Totp(key) ? "111111" : "000000" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        var before = factory.Emails.Messages.Count;
        var enabled = await admin.PostAsync("/api/v1/auth/two-factor/enable", new { code = Totp(key) });
        Assert.Equal(10, enabled.GetProperty("recoveryCodes").GetArrayLength());
        Assert.Contains(factory.Emails.Messages.Skip(before), m => m.To == admin.Email && m.Subject == "Two-step sign-in is on");

        // Still signed in on this browser after turning it on.
        status = await admin.GetAsync("/api/v1/auth/two-factor");
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(10, status.GetProperty("recoveryCodesLeft").GetInt32());
        // An admin can't turn it off: the role needs it.
        var off = await admin.Http.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { password = TestClient.Password }, TestClient.Json);
        off.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("two_factor_required", await off.ProblemCodeAsync());
    }

    [Fact]
    public async Task Signing_in_asks_for_a_code_after_the_password_and_accepts_each_recovery_code_once()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ajah Homes");
        var (key, codes) = await EnableAsync(admin);

        var browser = TestClient.Anonymous(factory);
        var step1 = await PasswordStepAsync(browser, admin.Email);
        step1.ShouldBe(HttpStatusCode.OK);
        Assert.True((await step1.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json)).GetProperty("requiresTwoFactor").GetBoolean());
        // The password alone doesn't sign in.
        (await browser.Http.GetAsync("/api/v1/auth/me")).ShouldBe(HttpStatusCode.Unauthorized);

        (await browser.Http.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { code = "12345" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.Unauthorized);
        var signedIn = await browser.Http.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { code = Totp(key)[..3] + " " + Totp(key)[3..] }, TestClient.Json);
        signedIn.ShouldBe(HttpStatusCode.OK);
        (await browser.Http.GetAsync("/api/v1/auth/me")).ShouldBe(HttpStatusCode.OK);

        // Without the password step first, a code alone does nothing.
        var stranger = TestClient.Anonymous(factory);
        var expired = await stranger.Http.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { code = Totp(key) }, TestClient.Json);
        expired.ShouldBe(HttpStatusCode.Unauthorized);
        Assert.Equal("two_factor_expired", await expired.ProblemCodeAsync());

        // A lost phone: a recovery code works once, and the owner is told.
        var lost = TestClient.Anonymous(factory);
        await PasswordStepAsync(lost, admin.Email);
        var before = factory.Emails.Messages.Count;
        (await lost.Http.PostAsJsonAsync("/api/v1/auth/login/recovery-code", new { code = codes[0] }, TestClient.Json)).ShouldBe(HttpStatusCode.OK);
        Assert.Contains(factory.Emails.Messages.Skip(before), m => m.To == admin.Email && m.Body.Contains("You have 9 left"));
        var again = TestClient.Anonymous(factory);
        await PasswordStepAsync(again, admin.Email);
        (await again.Http.PostAsJsonAsync("/api/v1/auth/login/recovery-code", new { code = codes[0] }, TestClient.Json))
            .ShouldBe(HttpStatusCode.Unauthorized);

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("action").GetString() == "user.two_factor_enabled");
    }

    [Fact]
    public async Task Required_roles_are_shut_out_after_the_grace_period_until_they_turn_it_on()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Lekki Gardens");
        (await admin.Http.GetAsync("/api/v1/properties")).ShouldBe(HttpStatusCode.OK);

        // Eight days later, with the default seven-day grace period.
        await using (var connection = new SqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE OrganizationMemberships SET TwoFactorRequiredSinceUtc = DATEADD(day, -8, SYSUTCDATETIME()) WHERE UserId = @user";
            command.Parameters.AddWithValue("@user", admin.UserId);
            await command.ExecuteNonQueryAsync();
        }
        var blocked = await admin.Http.GetAsync("/api/v1/properties");
        blocked.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal("two_factor_required", await blocked.ProblemCodeAsync());
        // Account pages stay open so they can turn it on.
        (await admin.Http.GetAsync("/api/v1/auth/me")).ShouldBe(HttpStatusCode.OK);
        await EnableAsync(admin);
        (await admin.Http.GetAsync("/api/v1/properties")).ShouldBe(HttpStatusCode.OK);

        // Other roles don't need it, but can turn it on and off with their password.
        var agent = await admin.AddMemberAsync(factory, "Agent");
        Assert.False((await agent.GetAsync("/api/v1/auth/two-factor")).GetProperty("required").GetBoolean());
        await EnableAsync(agent);
        (await agent.Http.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { password = "wrong-password-1" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await agent.Http.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { password = TestClient.Password }, TestClient.Json))
            .ShouldBe(HttpStatusCode.NoContent);
        Assert.False((await agent.GetAsync("/api/v1/auth/two-factor")).GetProperty("enabled").GetBoolean());

        // Promoting the agent to accountant starts their grace period; they keep working meanwhile.
        var promote = await admin.Http.PatchAsJsonAsync(
            $"/api/v1/organizations/{admin.OrganizationId}/members/{await MembershipIdAsync(admin, agent.UserId)}",
            new { roles = new[] { "Accountant" } }, TestClient.Json);
        await promote.EnsureSuccessAsync();
        Assert.True((await agent.GetAsync("/api/v1/auth/two-factor")).GetProperty("required").GetBoolean());
        (await agent.Http.GetAsync("/api/v1/properties")).ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> MembershipIdAsync(TestClient admin, Guid userId)
    {
        var members = await admin.GetAsync($"/api/v1/organizations/{admin.OrganizationId}/members");
        var list = members.ValueKind == JsonValueKind.Array ? members : members.GetProperty("items");
        return list.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == userId).GetProperty("membershipId").GetGuid();
    }
}
