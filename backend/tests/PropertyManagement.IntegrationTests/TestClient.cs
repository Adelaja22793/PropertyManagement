using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PropertyManagement.IntegrationTests;

/// <summary>One signed-in person: an HttpClient with its own cookie jar.</summary>
public sealed class TestClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private TestClient(HttpClient http) => Http = http;

    public HttpClient Http { get; }
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = "";
    public Guid OrganizationId { get; private set; }

    public static TestClient Anonymous(ApiFactory factory)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        return new TestClient(http);
    }

    /// <summary>Registers a new user who founds a new organization.</summary>
    public static async Task<TestClient> RegisterWithOrganizationAsync(ApiFactory factory, string organizationName)
    {
        var client = Anonymous(factory);
        var email = $"{Guid.NewGuid():N}@example.com";
        var response = await client.Http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = $"Admin of {organizationName}", email, password = Password, organizationName,
        });
        await response.EnsureSuccessAsync();
        var me = (await response.Content.ReadFromJsonAsync<JsonElement>(Json));
        client.UserId = me.GetProperty("userId").GetGuid();
        client.Email = email;
        client.OrganizationId = me.GetProperty("organizations")[0].GetProperty("id").GetGuid();
        client.UseOwnOrganization();
        return client;
    }

    /// <summary>Registers a new user who joins through an invitation.</summary>
    public static async Task<TestClient> RegisterWithInvitationAsync(ApiFactory factory, string email, string token)
    {
        var client = Anonymous(factory);
        var response = await client.Http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Invited Person", email, password = Password, invitationToken = token,
        });
        await response.EnsureSuccessAsync();
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.UserId = me.GetProperty("userId").GetGuid();
        client.Email = email;
        client.OrganizationId = me.GetProperty("organizations")[0].GetProperty("id").GetGuid();
        client.UseOwnOrganization();
        return client;
    }

    public const string Password = "correct-horse-42";

    /// <summary>Sends this organization in the X-Organization-Id header. Does not change <see cref="OrganizationId"/>.</summary>
    public void UseOrganization(Guid organizationId)
    {
        Http.DefaultRequestHeaders.Remove("X-Organization-Id");
        Http.DefaultRequestHeaders.Add("X-Organization-Id", organizationId.ToString());
    }

    /// <summary>Goes back to the organization this person registered with or joined.</summary>
    public void UseOwnOrganization() => UseOrganization(OrganizationId);

    public void ClearOrganization() => Http.DefaultRequestHeaders.Remove("X-Organization-Id");

    public async Task<JsonElement> CreateBranchAsync(string name)
    {
        var response = await Http.PostAsJsonAsync("/api/v1/branches", new { name, address = "Lagos" });
        await response.EnsureSuccessAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public async Task<(Guid InvitationId, string Token)> InviteAsync(string email, string role, params Guid[] branchIds)
    {
        var response = await Http.PostAsJsonAsync($"/api/v1/organizations/{OrganizationId}/invitations",
            new { email, role, branchIds });
        await response.EnsureSuccessAsync();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return (body.GetProperty("invitation").GetProperty("id").GetGuid(), body.GetProperty("acceptToken").GetString()!);
    }
}

public static class HttpResponseExtensions
{
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: " +
                await response.Content.ReadAsStringAsync());
    }

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static void ShouldBe(this HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} but got {(int)response.StatusCode}: {response.Content.ReadAsStringAsync().Result}");
}
