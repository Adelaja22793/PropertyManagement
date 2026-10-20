using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropertyManagement.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace PropertyManagement.IntegrationTests;

/// <summary>
/// Runs the real API against a real SQL Server, with a fresh database per test class that is
/// dropped afterwards. Point PMS_TEST_SQL at any SQL Server (a local container works).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultServer =
        "Server=localhost,1433;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True";

    private readonly string _database = $"pms_test_{Guid.NewGuid():N}";
    private readonly string _files = Path.Combine(Path.GetTempPath(), $"pms_files_{Guid.NewGuid():N}");

    public string ConnectionString { get; }

    /// <summary>Every email the API sent during this test class.</summary>
    public TestEmailSender Emails { get; } = new();

    public ApiFactory()
    {
        var server = Environment.GetEnvironmentVariable("PMS_TEST_SQL") ?? DefaultServer;
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["RateLimiting:AuthPermitPerMinute"] = "10000",
            ["RateLimiting:PublicPermitPerMinute"] = "10000",
            ["RateLimiting:EnquiryPermitPerMinute"] = "10000",
            // Tests run the lease schedule themselves, so it never races a test's own changes.
            ["Jobs:Leases:Enabled"] = "false",
            ["Storage:LocalPath"] = _files,
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public Task InitializeAsync()
    {
        _ = Server; // starts the host, which applies migrations
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_files)) Directory.Delete(_files, recursive: true);
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];";
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class TestEmailSender : IEmailSender
{
    public sealed record Sent(string To, string Subject, string Body);

    public ConcurrentQueue<Sent> Messages { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        Messages.Enqueue(new Sent(to, subject, body));
        return Task.CompletedTask;
    }
}
