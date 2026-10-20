using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Infrastructure.Persistence;

/// <summary>Counts the open <see cref="ICrossOrganizationAccess.Allow"/> scopes for this request.</summary>
public sealed class CrossOrganizationAccess : ICrossOrganizationAccess
{
    private int _open;

    public bool IsAllowed => _open > 0;

    public IDisposable Allow()
    {
        _open++;
        return new Scope(this);
    }

    private sealed class Scope(CrossOrganizationAccess owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._open--;
        }
    }
}

/// <summary>
/// Defence layer 5 (design 4.4): before each command, tells SQL Server which organization the request acts for, by
/// setting <c>SESSION_CONTEXT('OrganizationId')</c> on the connection. The <c>rls.OrganizationIsolation</c> policy then
/// hides and refuses rows of any other organization, even for raw SQL or a query that forgot its filter. Work with no
/// organization (sign-in, background jobs between organizations, deliberate cross-organization lookups) runs with
/// the value cleared.
/// </summary>
public sealed class RowLevelSecurityInterceptor(ICurrentOrganization currentOrganization, CrossOrganizationAccess crossOrganization)
    : DbCommandInterceptor, IDbConnectionInterceptor
{
    public const string ContextKey = "OrganizationId";

    /// <summary>What each open connection was last told. Pooled connections are reset, so a newly opened one holds nothing.</summary>
    private static readonly ConditionalWeakTable<DbConnection, StrongBox<Guid?>> Applied = new();

    private Guid? Wanted => crossOrganization.IsAllowed ? null : currentOrganization.OrganizationId;

    public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Applied.AddOrUpdate(connection, new(null));

    public Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        Applied.AddOrUpdate(connection, new(null));
        return Task.CompletedTask;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Apply(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        await ApplyAsync(command, ct);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Apply(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        await ApplyAsync(command, ct);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Apply(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken ct = default)
    {
        await ApplyAsync(command, ct);
        return result;
    }

    private void Apply(DbCommand command)
    {
        if (Pending(command) is not var (applied, wanted)) return;
        using var set = SetCommand(command, wanted);
        set.ExecuteNonQuery();
        applied.Value = wanted;
    }

    private async Task ApplyAsync(DbCommand command, CancellationToken ct)
    {
        if (Pending(command) is not var (applied, wanted)) return;
        await using var set = SetCommand(command, wanted);
        await set.ExecuteNonQueryAsync(ct);
        applied.Value = wanted;
    }

    /// <summary>The organization the connection must switch to, or null when it already holds the right one.</summary>
    private (StrongBox<Guid?> Applied, Guid? Wanted)? Pending(DbCommand command)
    {
        var connection = command.Connection;
        if (connection is null || connection.State != ConnectionState.Open) return null;
        var wanted = Wanted;
        var applied = Applied.GetValue(connection, _ => new(null));
        return applied.Value == wanted ? null : (applied, wanted);
    }

    private static DbCommand SetCommand(DbCommand command, Guid? wanted)
    {
        var set = command.Connection!.CreateCommand();
        set.Transaction = command.Transaction;
        set.CommandText = "EXEC sp_set_session_context @key = @key, @value = @value;";
        set.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 128) { Value = ContextKey });
        set.Parameters.Add(new SqlParameter("@value", SqlDbType.UniqueIdentifier) { Value = (object?)wanted ?? DBNull.Value });
        return set;
    }
}
