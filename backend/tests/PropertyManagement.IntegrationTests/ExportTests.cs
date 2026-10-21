using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace PropertyManagement.IntegrationTests;

/// <summary>Whole-company and per-person data exports (design 4.8 and 11.4).</summary>
public sealed class ExportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static async Task<Dictionary<string, byte[]>> DownloadZipAsync(TestClient client, string url)
    {
        var response = await client.Http.GetAsync(url);
        response.ShouldBe(HttpStatusCode.OK);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>();
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            entries[entry.FullName] = copy.ToArray();
        }
        return entries;
    }

    private static string Text(Dictionary<string, byte[]> zip, string name) => Encoding.UTF8.GetString(zip[name]).TrimStart('﻿');

    private static async Task UploadAsync(TestClient client, string subject, Guid subjectId, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(subject), "subject");
        form.Add(new StringContent(subjectId.ToString()), "subjectId");
        form.Add(new StringContent("Agreement"), "category");
        form.Add(new StringContent("false"), "sharedWithOwner");
        form.Add(new StringContent("false"), "sharedWithTenant");
        await (await client.Http.PostAsync("/api/v1/documents", form)).EnsureSuccessAsync();
    }

    [Fact]
    public async Task An_admin_downloads_every_table_of_their_own_company_without_identity_numbers()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Gbagada Homes");
        var code = $"GB-{Guid.NewGuid().ToString()[..4]}";
        var property = await admin.CreatePropertyAsync(code);
        await admin.CreateUnitAsync(property, "F1");
        var tenant = await admin.CreateTenantAsync("Chidi Okafor", $"{Guid.NewGuid():N}@example.com");
        await admin.PutAsync($"/api/v1/tenants/{tenant}/kyc", new { idType = "Nin", idNumber = "12345678901", bvn = "22233344455" });
        await UploadAsync(admin, "Property", property, "Deed.pdf");

        var other = await TestClient.RegisterWithOrganizationAsync(factory, "Other Company");
        var otherCode = $"OT-{Guid.NewGuid().ToString()[..4]}";
        await other.CreatePropertyAsync(otherCode);

        var zip = await DownloadZipAsync(admin, "/api/v1/exports/organization");
        Assert.Contains("README.txt", zip.Keys);
        Assert.Contains("Gbagada Homes", Text(zip, "organization.csv"));
        Assert.Contains(admin.Email, Text(zip, "members.csv"));
        var properties = Text(zip, "tables/properties.csv");
        Assert.Contains(code, properties);
        Assert.DoesNotContain(otherCode, properties);
        Assert.StartsWith("Id,", properties);
        Assert.DoesNotContain("RowVersion", properties);
        Assert.Contains("Chidi Okafor", Text(zip, "tables/tenants.csv"));
        // Identity numbers stay encrypted in the database and out of the export; the last four digits are there.
        var kyc = Text(zip, "tables/tenant-kyc.csv");
        Assert.DoesNotContain("Encrypted", kyc);
        Assert.Contains("8901", kyc);
        Assert.DoesNotContain(zip.Values, bytes => Encoding.UTF8.GetString(bytes).Contains("12345678901"));
        Assert.DoesNotContain("StorageKey", Text(zip, "tables/documents.csv"));
        Assert.DoesNotContain(zip.Keys, k => k.StartsWith("files/"));

        var withFiles = await DownloadZipAsync(admin, "/api/v1/exports/organization?includeFiles=true");
        Assert.Equal(Pdf, withFiles.Single(e => e.Key.StartsWith("files/property/") && e.Key.EndsWith("-Deed.pdf")).Value);

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Equal(2, audit.GetProperty("items").EnumerateArray().Count(a => a.GetProperty("action").GetString() == "data.exported"));
    }

    [Fact]
    public async Task A_tenant_or_owner_export_holds_only_that_person_and_needs_an_admin()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Yaba Lettings");
        var property = await admin.CreatePropertyAsync($"YL-{Guid.NewGuid().ToString()[..4]}");
        var unit = await admin.CreateUnitAsync(property, "A1");
        var tenant = await admin.CreateTenantAsync("Amaka Eze", $"{Guid.NewGuid():N}@example.com");
        var neighbour = await admin.CreateTenantAsync("Bola Ade", $"{Guid.NewGuid():N}@example.com");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
        await admin.CreateLeaseAsync(unit, tenant, today.AddDays(-10), today.AddDays(355));
        await admin.PostAsync($"/api/v1/tenants/{tenant}/guarantors", new { fullName = "Uche Eze", phone = "08030000002", relationship = "Brother" });
        await UploadAsync(admin, "Tenant", tenant, "Reference.pdf");
        await UploadAsync(admin, "Tenant", neighbour, "Neighbour.pdf");

        var zip = await DownloadZipAsync(admin, $"/api/v1/exports/tenants/{tenant}");
        Assert.Contains("Amaka Eze", Text(zip, "tenant.csv"));
        Assert.DoesNotContain("Bola Ade", Text(zip, "tenant.csv"));
        Assert.Contains("Uche Eze", Text(zip, "guarantors.csv"));
        Assert.Equal(2, Text(zip, "leases.csv").Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(zip.Keys, k => k.EndsWith("-Reference.pdf"));
        Assert.DoesNotContain(zip.Keys, k => k.EndsWith("-Neighbour.pdf"));

        var owner = await admin.CreateOwnerAsync("Funke Bello", $"{Guid.NewGuid():N}@example.com");
        var ownerZip = await DownloadZipAsync(admin, $"/api/v1/exports/owners/{owner}");
        Assert.Contains("Funke Bello", Text(ownerZip, "owner.csv"));

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("action").GetString() == "person.exported" && a.GetProperty("resourceId").GetString() == tenant.ToString());

        // Only admins export, and only their own organization's people.
        var manager = await admin.AddMemberAsync(factory, "BranchManager");
        (await manager.Http.GetAsync($"/api/v1/exports/tenants/{tenant}")).ShouldBe(HttpStatusCode.Forbidden);
        (await manager.Http.GetAsync("/api/v1/exports/organization")).ShouldBe(HttpStatusCode.Forbidden);
        var outsider = await TestClient.RegisterWithOrganizationAsync(factory, "Outsider Homes");
        (await outsider.Http.GetAsync($"/api/v1/exports/tenants/{tenant}")).ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Http.GetAsync($"/api/v1/exports/owners/{owner}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_guarantor_export_holds_their_record_and_only_the_terms_they_guarantee()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Surety Homes");
        var property = await admin.CreatePropertyAsync($"SU-{Guid.NewGuid().ToString()[..4]}");
        var unit = await admin.CreateUnitAsync(property, "B2");
        var tenant = await admin.CreateTenantAsync("Ngozi Obi", $"{Guid.NewGuid():N}@example.com");
        var guarantor = (await admin.PostAsync($"/api/v1/tenants/{tenant}/guarantors",
            new { fullName = "Chidi Obi", phone = "08030000003", relationship = "Father", employer = "Lagos State Govt" })).GetProperty("id").GetGuid();
        var other = (await admin.PostAsync($"/api/v1/tenants/{tenant}/guarantors",
            new { fullName = "Ada Obi", phone = "08030000004", relationship = "Aunt" })).GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
        var lease = await admin.PostAsync("/api/v1/leases",
            TestClient.LeaseBody(unit, [tenant], today.AddDays(-10), today.AddDays(355), guarantorIds: [guarantor]));

        var zip = await DownloadZipAsync(admin, $"/api/v1/exports/guarantors/{guarantor}");
        Assert.Contains("Chidi Obi", Text(zip, "guarantor.csv"));
        Assert.Contains("Lagos State Govt", Text(zip, "guarantor.csv"));
        Assert.DoesNotContain("Ada Obi", Text(zip, "guarantor.csv"));
        var leases = Text(zip, "guaranteed-leases.csv");
        Assert.Contains(lease.GetProperty("reference").GetString()!, leases);
        Assert.Contains("Ngozi Obi", leases);
        Assert.Contains("guarantor form", Text(zip, "README.txt"));
        Assert.DoesNotContain(zip.Keys, k => k is "tenant.csv" or "identity.csv" or "documents.csv");

        var otherZip = await DownloadZipAsync(admin, $"/api/v1/exports/guarantors/{other}");
        Assert.Single(Text(otherZip, "guaranteed-leases.csv").Split("\r\n", StringSplitOptions.RemoveEmptyEntries));

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("action").GetString() == "person.exported" && a.GetProperty("resourceId").GetString() == guarantor.ToString());

        var manager = await admin.AddMemberAsync(factory, "BranchManager");
        (await manager.Http.GetAsync($"/api/v1/exports/guarantors/{guarantor}")).ShouldBe(HttpStatusCode.Forbidden);
        var outsider = await TestClient.RegisterWithOrganizationAsync(factory, "Elsewhere Homes");
        (await outsider.Http.GetAsync($"/api/v1/exports/guarantors/{guarantor}")).ShouldBe(HttpStatusCode.NotFound);
    }
}
