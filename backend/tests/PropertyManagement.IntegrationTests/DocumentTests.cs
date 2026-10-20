using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Documents and photos: upload checks, who can read what, and what the portals see.</summary>
public sealed class DocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private TestClient _admin = null!;
    private Guid _property;
    private Guid _otherProperty;
    private Guid _ownerId;
    private Guid _tenantId;
    private Guid _lease;
    private Guid _request;
    private TestClient _owner = null!;
    private TestClient _tenant = null!;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Document Homes");
        _property = await _admin.CreatePropertyAsync($"DH-{Guid.NewGuid().ToString()[..4]}");
        _otherProperty = await _admin.CreatePropertyAsync($"DX-{Guid.NewGuid().ToString()[..4]}");
        var unit = await _admin.CreateUnitAsync(_property, "2B");

        var ownerEmail = $"{Guid.NewGuid():N}@owner.example.com";
        _ownerId = await _admin.CreateOwnerAsync("Alhaji Bello", ownerEmail);
        await _admin.PutAsync($"/api/v1/properties/{_property}/owners", new
        {
            effectiveFrom = Today.AddYears(-1), shares = new[] { new { ownerId = _ownerId, sharePercent = 100m } },
        });
        var ownerInvite = await _admin.PostAsync($"/api/v1/owners/{_ownerId}/portal-invite", new { });
        _owner = await TestClient.RegisterWithInvitationAsync(factory, ownerEmail, ownerInvite.GetProperty("acceptToken").GetString()!);

        var tenantEmail = $"{Guid.NewGuid():N}@tenant.example.com";
        _tenantId = await _admin.CreateTenantAsync("Ngozi Obi", tenantEmail);
        _lease = Id(await _admin.CreateLeaseAsync(unit, _tenantId, Today.AddMonths(-1)));
        var tenantInvite = await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/portal-invite", new { });
        _tenant = await TestClient.RegisterWithInvitationAsync(factory, tenantEmail, tenantInvite.GetProperty("acceptToken").GetString()!);
        _request = Id(await _tenant.PostAsync("/api/v1/portal/tenant/maintenance", new
        {
            leaseId = _lease, title = "Window won't close", category = "Structural", urgency = "Normal",
        }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string subject, Guid subjectId, string category = "Other",
        string? title = null, DateOnly? expiresOn = null, bool owner = false, bool tenant = false)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(subject), "subject");
        form.Add(new StringContent(subjectId.ToString()), "subjectId");
        form.Add(new StringContent(category), "category");
        if (title is not null) form.Add(new StringContent(title), "title");
        if (expiresOn is { } d) form.Add(new StringContent(d.ToString("yyyy-MM-dd")), "expiresOn");
        form.Add(new StringContent(owner ? "true" : "false"), "sharedWithOwner");
        form.Add(new StringContent(tenant ? "true" : "false"), "sharedWithTenant");
        return form;
    }

    private static async Task<JsonElement> UploadAsync(TestClient client, MultipartFormDataContent form)
    {
        var response = await client.Http.PostAsync("/api/v1/documents", form);
        await response.EnsureSuccessAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json);
    }

    private static string ContentUrl(TestClient client, Guid id) => $"/api/v1/organizations/{client.OrganizationId}/documents/{id}/content";

    private static async Task<string?> UploadCodeAsync(TestClient client, MultipartFormDataContent form, HttpStatusCode expected)
    {
        var response = await client.Http.PostAsync("/api/v1/documents", form);
        response.ShouldBe(expected);
        return await response.ProblemCodeAsync();
    }

    [Fact]
    public async Task Uploads_are_type_checked_size_limited_and_virus_scanned()
    {
        var saved = await UploadAsync(_admin, Form(Pdf, "Title deed.PDF", "Property", _property, "Certificate", title: "Certificate of occupancy"));
        Assert.Equal("application/pdf", saved.GetProperty("contentType").GetString());
        Assert.Equal("Title deed.pdf", saved.GetProperty("fileName").GetString());

        // A web page renamed to .pdf, a macro file and a script are refused by their bytes, not their names.
        Assert.Equal("file_type", await UploadCodeAsync(_admin,
            Form("<html><script>alert(1)</script></html>"u8.ToArray(), "deed.pdf", "Property", _property), HttpStatusCode.UnsupportedMediaType));
        Assert.Equal("file_type", await UploadCodeAsync(_admin,
            Form([0x4D, 0x5A, 0x90, 0x00], "setup.png", "Property", _property), HttpStatusCode.UnsupportedMediaType));

        // The EICAR test file stands in for a virus.
        var infected = Pdf.Concat(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray()).ToArray();
        Assert.Equal("file_rejected", await UploadCodeAsync(_admin, Form(infected, "invoice.pdf", "Property", _property), (HttpStatusCode)422));

        var tooBig = new byte[10 * 1024 * 1024 + 1];
        Pdf.CopyTo(tooBig, 0);
        Assert.Equal("file_too_large", await UploadCodeAsync(_admin, Form(tooBig, "big.pdf", "Property", _property), HttpStatusCode.RequestEntityTooLarge));

        // Only images can be photos, and records must exist and be visible.
        await UploadCodeAsync(_admin, Form(Pdf, "a.pdf", "Property", _property, "Photo"), HttpStatusCode.BadRequest);
        await UploadCodeAsync(_admin, Form(Pdf, "a.pdf", "Property", Guid.NewGuid()), HttpStatusCode.NotFound);

        var list = await _admin.GetAsync($"/api/v1/documents?subject=Property&subjectId={_property}");
        Assert.Single(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Downloads_check_access_on_every_request()
    {
        var doc = Id(await UploadAsync(_admin, Form(Pdf, "plan.pdf", "Property", _property, "Other", "Floor plan")));
        var response = await _admin.Http.GetAsync(ContentUrl(_admin, doc));
        response.ShouldBe(HttpStatusCode.OK);
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("attachment", (await _admin.Http.GetAsync(ContentUrl(_admin, doc) + "?download=true")).Content.Headers.ContentDisposition!.DispositionType);

        // Another company can't reach it, through its own organization or ours.
        var outsider = await TestClient.RegisterWithOrganizationAsync(factory, "Other Lettings");
        (await outsider.Http.GetAsync($"/api/v1/organizations/{outsider.OrganizationId}/documents/{doc}/content")).ShouldBe(HttpStatusCode.NotFound);
        outsider.ClearOrganization();
        (await outsider.Http.GetAsync(ContentUrl(_admin, doc))).ShouldBe(HttpStatusCode.Forbidden);
        (await TestClient.Anonymous(factory).Http.GetAsync(ContentUrl(_admin, doc))).ShouldBe(HttpStatusCode.Unauthorized);

        // A property manager sees only their assigned properties' documents.
        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        (await manager.Http.GetAsync(ContentUrl(_admin, doc))).ShouldBe(HttpStatusCode.NotFound);
        (await manager.Http.GetAsync($"/api/v1/documents?subject=Property&subjectId={_property}")).ShouldBe(HttpStatusCode.NotFound);
        var managed = await _admin.CreatePropertyAsync($"DM-{Guid.NewGuid().ToString()[..4]}", managerUserId: manager.UserId);
        var managedDoc = Id(await UploadAsync(_admin, Form(Pdf, "plan.pdf", "Property", managed)));
        (await manager.Http.GetAsync(ContentUrl(_admin, managedDoc))).ShouldBe(HttpStatusCode.OK);

        // Identity documents need Tenants.ViewKyc, which a property manager doesn't have.
        var id = Id(await UploadAsync(_admin, Form(Png, "nin.png", "Tenant", _tenantId, "Identity", "NIN slip")));
        var managerList = await manager.GetAsync($"/api/v1/documents?subject=Tenant&subjectId={_tenantId}");
        Assert.Empty(managerList.GetProperty("items").EnumerateArray());
        (await manager.Http.GetAsync(ContentUrl(_admin, id))).ShouldBe(HttpStatusCode.NotFound);
        (await _admin.Http.GetAsync(ContentUrl(_admin, id))).ShouldBe(HttpStatusCode.OK);
        var audit = await _admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("action").GetString() == "document.identity_viewed");

        // Deleting needs Documents.Delete, removes the file and hides the record.
        (await manager.Http.DeleteAsync($"/api/v1/documents/{doc}")).ShouldBe(HttpStatusCode.Forbidden);
        (await _admin.Http.DeleteAsync($"/api/v1/documents/{doc}")).ShouldBe(HttpStatusCode.NoContent);
        (await _admin.Http.GetAsync(ContentUrl(_admin, doc))).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assigned_maintenance_staff_add_and_see_photos_on_their_jobs_only()
    {
        var staff = await _admin.AddMemberAsync(factory, "MaintenanceStaff");
        (await staff.Http.PostAsync("/api/v1/documents", Form(Png, "leak.png", "Maintenance", _request, "Photo"))).ShouldBe(HttpStatusCode.NotFound);

        await _admin.PostAsync($"/api/v1/maintenance/{_request}/review", new { });
        await _admin.PostAsync($"/api/v1/maintenance/{_request}/approve", new { });
        await _admin.PostAsync($"/api/v1/maintenance/{_request}/assign", new { assignedUserId = staff.UserId });
        var photo = Id(await UploadAsync(staff, Form(Png, "after.png", "Maintenance", _request, "Photo", tenant: true, owner: true)));
        (await staff.Http.GetAsync(ContentUrl(_admin, photo))).ShouldBe(HttpStatusCode.OK);
        // Their role gives no reach into property documents.
        (await staff.Http.GetAsync($"/api/v1/documents?subject=Property&subjectId={_property}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Portals_see_only_what_was_shared_with_them()
    {
        var shared = Id(await UploadAsync(_admin, Form(Pdf, "insurance.pdf", "Property", _property, "Insurance", "Building insurance",
            expiresOn: Today.AddDays(20), owner: true)));
        var internalDoc = Id(await UploadAsync(_admin, Form(Pdf, "valuation.pdf", "Property", _property, "Other", "Valuation")));
        var otherPropertyDoc = Id(await UploadAsync(_admin, Form(Pdf, "x.pdf", "Property", _otherProperty, "Other", "Elsewhere", owner: true)));
        var agreement = Id(await UploadAsync(_admin, Form(Pdf, "tenancy.pdf", "Lease", _lease, "Agreement", "Signed tenancy agreement", tenant: true, owner: true)));

        var ownerDocs = (await _owner.GetAsync("/api/v1/portal/owner/documents")).EnumerateArray().Select(Id).ToList();
        Assert.Contains(shared, ownerDocs);
        Assert.Contains(agreement, ownerDocs);
        Assert.DoesNotContain(internalDoc, ownerDocs);
        Assert.DoesNotContain(otherPropertyDoc, ownerDocs);
        (await _owner.Http.GetAsync(ContentUrl(_admin, shared))).ShouldBe(HttpStatusCode.OK);
        (await _owner.Http.GetAsync(ContentUrl(_admin, internalDoc))).ShouldBe(HttpStatusCode.NotFound);

        var tenantDocs = (await _tenant.GetAsync("/api/v1/portal/tenant/documents")).EnumerateArray().Select(Id).ToList();
        Assert.Equal([agreement], tenantDocs);
        (await _tenant.Http.GetAsync(ContentUrl(_admin, shared))).ShouldBe(HttpStatusCode.NotFound);

        // The tenant adds a photo to their repair; staff and the owner see it. Only photos are accepted.
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Png), "file", "window.png");
        var added = await _tenant.Http.PostAsync($"/api/v1/portal/tenant/maintenance/{_request}/photos", form);
        added.ShouldBe(HttpStatusCode.Created);
        var photo = Id(await added.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json));
        var pdfForm = new MultipartFormDataContent();
        pdfForm.Add(new ByteArrayContent(Pdf), "file", "window.pdf");
        (await _tenant.Http.PostAsync($"/api/v1/portal/tenant/maintenance/{_request}/photos", pdfForm)).ShouldBe(HttpStatusCode.UnsupportedMediaType);

        var staffList = await _admin.GetAsync($"/api/v1/documents?subject=Maintenance&subjectId={_request}");
        var item = Assert.Single(staffList.GetProperty("items").EnumerateArray());
        Assert.Equal("TenantPortal", item.GetProperty("source").GetString());
        Assert.Contains(photo, (await _owner.GetAsync("/api/v1/portal/owner/documents")).EnumerateArray().Select(Id));
        (await _owner.Http.GetAsync(ContentUrl(_admin, photo))).ShouldBe(HttpStatusCode.OK);
        (await _tenant.Http.GetAsync(ContentUrl(_admin, photo))).ShouldBe(HttpStatusCode.OK);

        // Staff can't upload through the portal, and portal users can't use the staff endpoints.
        (await _tenant.Http.PostAsync("/api/v1/documents", Form(Png, "a.png", "Lease", _lease, "Photo"))).ShouldBe(HttpStatusCode.Forbidden);
        (await _owner.Http.GetAsync($"/api/v1/documents?subject=Property&subjectId={_property}")).ShouldBe(HttpStatusCode.Forbidden);

        // Expiring documents show up for staff.
        var expiring = await _admin.GetAsync("/api/v1/documents/expiring?days=30");
        Assert.Contains(shared, expiring.EnumerateArray().Select(Id));
    }
}
