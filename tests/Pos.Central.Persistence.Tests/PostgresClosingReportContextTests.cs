using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pos.Application.Synchronization;
using Pos.Central.Persistence;
using Pos.Domain.StoreSessions;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresClosingReportContextTests
{
    private const string ReportId = "closing:6272616E6368:72657175657374";
    private static readonly DateOnly Day = new(2026, 11, 22);
    private static readonly StoreOperationContext Opened = new("opener", "device", new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero), Day);
    private static readonly ShiftClosing Closing = new("store", "shift", true, 500, 490, -10, Opened);
    private static string Payload() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        contextId = "lifecycle:" + ReportId,
        reportId = ReportId,
        branchId = "branch",
        requestId = "request",
        context = new
        {
            SchemaVersion = 1,
            BranchId = "branch",
            RequestId = "request",
            StoreSessionId = "store",
            ClosingShiftId = "shift",
            BusinessDate = Day,
            OpeningCashAmount = 500m,
            Opened,
            ClosesStore = true,
            Shifts = new[] { new { ShiftId = "shift", OpeningCashAmount = 500m, Opened, Closing } }
        }
    });
    private static string Parent() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        reportId = ReportId,
        branchId = "branch",
        requestId = "request",
        storeSessionId = "store",
        shiftId = "shift",
        operation = "CloseStore",
        businessDate = Day,
        closing = Closing,
        cashSummary = (object?)null
    });
    private static ClosingReportContextEnvelope Envelope() => ClosingReportContextEnvelope.Parse(Payload());
    [Fact]
    public async Task New_context_requires_parent_and_commit_before_acknowledgement()
    {
        var session = new Session { Inserted = true };
        var result = await new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope());
        Assert.Equal(ClosingReportContextPersistenceResult.Accepted, result);
        Assert.Equal(new[] { "parent", "insert", "commit", "dispose" }, session.Calls);
        Assert.Equal(Payload(), session.Record!.PayloadJson);
    }
    [Fact]
    public async Task Parent_missing_cannot_insert_or_acknowledge_context()
    {
        var session = new Session { ParentJson = null };
        Assert.Equal(ClosingReportContextPersistenceResult.ParentMissing,
            await new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.Equal(new[] { "parent", "dispose" }, session.Calls);
    }
    [Fact]
    public async Task Parent_closing_mismatch_is_conflict_without_insert()
    {
        var root = JsonNode.Parse(Parent())!; root["closing"]!["ActualCashAmount"] = 480; root["closing"]!["CashDifference"] = -20;
        var session = new Session { ParentJson = root.ToJsonString() };
        Assert.Equal(ClosingReportContextPersistenceResult.Conflict,
            await new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.Equal(new[] { "parent", "dispose" }, session.Calls);
    }
    [Fact]
    public async Task Exact_duplicate_confirms_only_after_commit()
    {
        var session = new Session { Matches = [PostgresClosingReportContextRecord.FromEnvelope(Envelope())] };
        Assert.Equal(ClosingReportContextPersistenceResult.AlreadyAccepted,
            await new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.Equal(new[] { "parent", "insert", "read", "commit", "dispose" }, session.Calls);
    }
    [Theory]
    [InlineData("body")]
    [InlineData("identity")]
    [InlineData("multiple")]
    public async Task Immutable_identity_conflicts_do_not_commit(string change)
    {
        var record = PostgresClosingReportContextRecord.FromEnvelope(Envelope());
        var changed = change == "body" ? record with { PayloadJson = record.PayloadJson + " " } : record with { ContextId = "other" };
        var session = new Session { Matches = change == "multiple" ? [record, changed] : [changed] };
        Assert.Equal(ClosingReportContextPersistenceResult.Conflict,
            await new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.DoesNotContain("commit", session.Calls);
    }
    [Theory]
    [InlineData("parent")]
    [InlineData("insert")]
    [InlineData("read")]
    [InlineData("commit")]
    public async Task Storage_failure_never_produces_acknowledgement(string stage)
    {
        var session = new Session { FailureStage = stage, Matches = [PostgresClosingReportContextRecord.FromEnvelope(Envelope())] };
        await Assert.ThrowsAsync<ClosingReportStoreUnavailableException>(() =>
            new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.Equal("dispose", session.Calls[^1]);
    }
    [Fact]
    public async Task Corrupt_stored_parent_is_unavailable_not_accepted()
    {
        var session = new Session { ParentJson = "{}" };
        await Assert.ThrowsAsync<ClosingReportStoreUnavailableException>(() =>
            new PostgresClosingReportContextIngestionStore(new Factory(session)).PersistAsync(Envelope()));
        Assert.DoesNotContain("insert", session.Calls);
    }
    [Fact]
    public async Task Caller_cancellation_prevents_opening_a_transaction()
    {
        var factory = new Factory(new Session()); using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PostgresClosingReportContextIngestionStore(factory).PersistAsync(Envelope(), source.Token));
        Assert.False(factory.Opened);
    }
    [Fact]
    public void Migration_three_preserves_existing_versions_and_defines_parent_identity()
    {
        var migrations = PostgresCentralMigrationCatalog.GetMigrations();
        Assert.Equal(new[] { 1, 2, 3 }, migrations.Select(migration => migration.Version));
        var migration = migrations[2]; Assert.Equal("0003_closing_report_contexts", migration.Name);
        Assert.Contains("context_id text PRIMARY KEY", migration.Sql); Assert.Contains("UNIQUE (report_id)", migration.Sql);
        Assert.Contains("REFERENCES public.closing_reports(report_id)", migration.Sql);
        Assert.Contains("payload_json text", migration.Sql); Assert.DoesNotContain("jsonb", migration.Sql);
        Assert.Equal(new[] { 3 }, PostgresCentralMigrationPlanner.Plan([new(1, "0001_branch_cloud_events"), new(2, "0002_closing_reports")]).Select(value => value.Version));
    }
    [Fact]
    public void SQL_boundaries_are_parameterized_and_immutable()
    {
        Assert.Contains("FOR KEY SHARE", PostgresClosingReportContextSessionContract.ReadParentSql);
        Assert.Contains("branch_id=@branch_id", PostgresClosingReportContextSessionContract.ReadParentSql);
        Assert.Contains("ON CONFLICT DO NOTHING", PostgresClosingReportContextSessionContract.TryInsertSql);
        Assert.DoesNotContain("UPDATE", PostgresClosingReportContextSessionContract.TryInsertSql);
        Assert.Contains("context_id=@context_id OR report_id=@report_id", PostgresClosingReportContextSessionContract.ReadIdentityMatchesSql);
        var ready = NpgsqlPostgresClosingReportContextReadinessProbe.InspectSql;
        Assert.Contains("NOT c.condeferrable", ready); Assert.Contains("actual.is_nullable='NO'", ready);
        Assert.Contains("c.confkey", ready); Assert.Contains("public.closing_reports", ready);
    }
    [Fact]
    public async Task Composition_is_idempotent_and_unreachable_storage_is_not_ready()
    {
        var services = new ServiceCollection();
        services.AddPostgresClosingReportContexts("Host=127.0.0.1;Port=1;Database=synthetic;Timeout=1");
        services.AddPostgresClosingReportContexts("Host=127.0.0.1;Port=1;Database=synthetic;Timeout=1");
        await using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IClosingReportContextIngestionStore>());
        Assert.False(await provider.GetRequiredService<IPostgresClosingReportContextReadinessProbe>().IsReadyAsync());
    }
    private sealed class Factory(Session session) : IPostgresClosingReportContextSessionFactory
    {
        internal bool Opened;
        public ValueTask<IPostgresClosingReportContextSession> OpenTransactionAsync(CancellationToken token = default)
        { Opened = true; token.ThrowIfCancellationRequested(); return ValueTask.FromResult<IPostgresClosingReportContextSession>(session); }
    }
    private sealed class Session : IPostgresClosingReportContextSession
    {
        internal string? ParentJson = Parent(); internal bool Inserted; internal string? FailureStage;
        internal IReadOnlyList<PostgresClosingReportContextRecord> Matches = [];
        internal PostgresClosingReportContextRecord? Record; internal List<string> Calls = [];
        private void Step(string name, CancellationToken token) { token.ThrowIfCancellationRequested(); Calls.Add(name); if (FailureStage == name) { throw new NpgsqlException("synthetic private storage failure"); } }
        public Task<string?> ReadParentAsync(string reportId, string branchId, CancellationToken token = default) { Step("parent", token); return Task.FromResult(ParentJson); }
        public Task<bool> TryInsertAsync(PostgresClosingReportContextRecord context, CancellationToken token = default) { Step("insert", token); Record = context; return Task.FromResult(Inserted); }
        public Task<IReadOnlyList<PostgresClosingReportContextRecord>> ReadIdentityMatchesAsync(PostgresClosingReportContextRecord context, CancellationToken token = default) { Step("read", token); return Task.FromResult(Matches); }
        public Task CommitAsync(CancellationToken token = default) { Step("commit", token); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Calls.Add("dispose"); return ValueTask.CompletedTask; }
    }
}
