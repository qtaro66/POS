using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pos.Application.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresClosingReportTests
{
    private static ClosingReportEnvelope Envelope() => ClosingReportEnvelope.Parse(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        reportId = "closing:" + Convert.ToHexString(Encoding.UTF8.GetBytes("branch")) + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes("request")),
        branchId = "branch",
        requestId = "request",
        storeSessionId = "store",
        shiftId = "shift",
        operation = "CloseStore",
        businessDate = "2026-11-21",
        closing = new
        {
            StoreSessionId = "store",
            ShiftId = "shift",
            ClosesStore = true,
            ExpectedCashAmount = 500,
            ActualCashAmount = 490,
            CashDifference = -10,
            Context = new { EmployeeId = "employee", DeviceId = "device", OccurredAt = "2026-11-21T03:00:00.1234567Z", BranchLocalDate = "2026-11-21" }
        },
        cashSummary = (object?)null
    }));

    [Fact]
    public async Task New_report_is_not_acknowledged_before_commit_and_disposal()
    {
        var report = Envelope();
        var session = new Session { Inserted = true };
        var result = await new PostgresClosingReportIngestionStore(new Factory(session)).PersistAsync(report);
        Assert.Equal(ClosingReportPersistenceResult.Accepted, result);
        Assert.Equal(new[] { "insert", "commit", "dispose" }, session.Calls);
        Assert.Equal(report.PayloadJson, session.Report!.PayloadJson);
        Assert.Equal(0, session.Report.OccurredAt.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Contains("1234567", session.Report.PayloadJson);
    }

    [Fact]
    public async Task Exact_retry_returns_already_accepted_after_commit()
    {
        var report = Envelope();
        var session = new Session { Matches = [PostgresClosingReportRecord.FromEnvelope(report)] };
        var result = await new PostgresClosingReportIngestionStore(new Factory(session)).PersistAsync(report);
        Assert.Equal(ClosingReportPersistenceResult.AlreadyAccepted, result);
        Assert.Equal(new[] { "insert", "read", "commit", "dispose" }, session.Calls);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("report")]
    [InlineData("branch")]
    [InlineData("request")]
    [InlineData("store")]
    [InlineData("shift")]
    [InlineData("operation")]
    [InlineData("multiple")]
    public async Task Changed_content_or_identity_conflicts_without_committing(string change)
    {
        var report = Envelope();
        var original = PostgresClosingReportRecord.FromEnvelope(report);
        var changed = change switch
        {
            "payload" => original with { PayloadJson = original.PayloadJson + " " },
            "report" => original with { ReportId = "other" },
            "branch" => original with { BranchId = "other" },
            "request" => original with { RequestId = "other" },
            "store" => original with { StoreSessionId = "other" },
            "shift" => original with { ShiftId = "other" },
            "operation" => original with { Operation = "CloseShift" },
            _ => original
        };
        var session = new Session { Matches = change == "multiple" ? [original, changed] : [changed] };
        var result = await new PostgresClosingReportIngestionStore(new Factory(session)).PersistAsync(report);
        Assert.Equal(ClosingReportPersistenceResult.Conflict, result);
        Assert.Equal(new[] { "insert", "read", "dispose" }, session.Calls);
    }

    [Fact]
    public async Task Missing_identity_after_conflict_fails_closed()
    {
        var session = new Session();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresClosingReportIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.DoesNotContain("commit", session.Calls);
        Assert.Contains("dispose", session.Calls);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("read")]
    [InlineData("commit")]
    public async Task Database_failure_never_returns_success_and_disposes_transaction(string operation)
    {
        var session = new Session { FailureAt = operation, Inserted = operation == "commit" };
        await Assert.ThrowsAsync<ClosingReportStoreUnavailableException>(() =>
            new PostgresClosingReportIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.Contains("dispose", session.Calls);
    }

    [Fact]
    public async Task Cancelled_ingestion_does_not_open_a_session()
    {
        var session = new Session();
        var factory = new Factory(session);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PostgresClosingReportIngestionStore(factory).PersistAsync(Envelope(), cancellation.Token));
        Assert.False(factory.Opened);
    }

    [Fact]
    public async Task Token_reaches_open_insert_and_commit()
    {
        var session = new Session { Inserted = true };
        var factory = new Factory(session);
        using var cancellation = new CancellationTokenSource();
        await new PostgresClosingReportIngestionStore(factory).PersistAsync(Envelope(), cancellation.Token);
        Assert.Equal(cancellation.Token, factory.Token);
        Assert.All(session.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task Composition_shares_connection_factory_and_is_idempotent_without_network()
    {
        var services = new ServiceCollection();
        const string connection = "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";
        services.AddPostgresBranchCloudEventIngestion(connection);
        services.AddPostgresClosingReportIngestion(connection);
        services.AddPostgresClosingReportIngestion(connection);
        await using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<PostgresCentralConnectionFactory>());
        Assert.Single(provider.GetServices<IClosingReportIngestionStore>());
        Assert.Single(provider.GetServices<IPostgresClosingReportSessionFactory>());
        Assert.IsType<PostgresClosingReportIngestionStore>(provider.GetRequiredService<IClosingReportIngestionStore>());
        Assert.IsType<NpgsqlPostgresClosingReportReadinessProbe>(provider.GetRequiredService<IPostgresClosingReportReadinessProbe>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<IPostgresClosingReportReadinessProbe>().IsReadyAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.GetRequiredService<IPostgresClosingReportSessionFactory>().OpenTransactionAsync(cancellation.Token));
    }

    [Fact]
    public void Sql_preserves_original_json_and_only_inserts_immutable_parameterized_records()
    {
        var insert = PostgresClosingReportSessionContract.TryInsertSql;
        Assert.Contains("INSERT INTO public.closing_reports", insert);
        Assert.Contains("ON CONFLICT DO NOTHING", insert);
        Assert.DoesNotContain("UPDATE", insert, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@payload_json", insert);
        var read = PostgresClosingReportSessionContract.ReadIdentityMatchesSql;
        Assert.Contains("report_id=@report_id", read);
        Assert.Contains("request_id=@request_id", read);
        Assert.Contains("store_session_id=@store_session_id AND shift_id=@shift_id", read);
        var migration = Assert.Single(PostgresCentralMigrationCatalog.GetMigrations(), value => value.Version == 2);
        Assert.Contains("payload_json text NOT NULL", migration.Sql);
        Assert.Contains("UNIQUE (branch_id, request_id)", migration.Sql);
        Assert.Contains("UNIQUE (branch_id, store_session_id, shift_id)", migration.Sql);
        Assert.Contains("VALUES (2, '0002_closing_reports')", migration.Sql);
        Assert.DoesNotContain("UPDATE", migration.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", migration.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readiness_requires_version_columns_types_and_all_identity_constraints()
    {
        var sql = NpgsqlPostgresClosingReportReadinessProbe.InspectSql;
        Assert.Contains("public.central_schema_migrations", sql);
        Assert.Contains("public.closing_reports", sql);
        Assert.Contains("information_schema.columns", sql);
        Assert.Contains("actual.udt_name=required.udt_name", sql);
        Assert.Contains("actual.is_nullable='NO'", sql);
        Assert.Contains("ARRAY['report_id']", sql);
        Assert.Contains("ARRAY['branch_id','request_id']", sql);
        Assert.Contains("ARRAY['branch_id','store_session_id','shift_id']", sql);
        Assert.Contains("c.convalidated", sql);
        Assert.Contains("NOT c.condeferrable", sql);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Version_one_upgrade_contains_only_the_new_migration()
    {
        var pending = PostgresCentralMigrationPlanner.Plan([new(1, "0001_branch_cloud_events")]);
        Assert.Equal(2, Assert.Single(pending).Version);
    }

    private sealed class Factory(Session session) : IPostgresClosingReportSessionFactory
    {
        internal bool Opened { get; private set; }
        internal CancellationToken Token { get; private set; }
        public ValueTask<IPostgresClosingReportSession> OpenTransactionAsync(CancellationToken token = default)
        {
            Opened = true; Token = token;
            return ValueTask.FromResult<IPostgresClosingReportSession>(session);
        }
    }
    private sealed class Session : IPostgresClosingReportSession
    {
        internal bool Inserted { get; init; }
        internal string? FailureAt { get; init; }
        internal IReadOnlyList<PostgresClosingReportRecord> Matches { get; init; } = [];
        internal PostgresClosingReportRecord? Report { get; private set; }
        internal List<string> Calls { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];
        public Task<bool> TryInsertAsync(PostgresClosingReportRecord report, CancellationToken token = default)
        {
            Record("insert", token); Report = report; return Task.FromResult(Inserted);
        }
        public Task<IReadOnlyList<PostgresClosingReportRecord>> ReadIdentityMatchesAsync(PostgresClosingReportRecord report, CancellationToken token = default)
        {
            Record("read", token); return Task.FromResult(Matches);
        }
        public Task CommitAsync(CancellationToken token = default) { Record("commit", token); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Calls.Add("dispose"); return ValueTask.CompletedTask; }
        private void Record(string operation, CancellationToken token)
        {
            Calls.Add(operation); Tokens.Add(token); token.ThrowIfCancellationRequested();
            if (FailureAt == operation) { throw new NpgsqlException("Synthetic database failure"); }
        }
    }
}
