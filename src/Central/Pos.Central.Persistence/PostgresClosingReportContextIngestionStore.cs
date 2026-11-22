using Npgsql;
using Pos.Application.Synchronization;

namespace Pos.Central.Persistence;

public sealed record PostgresClosingReportContextRecord(string ContextId, string ReportId, string BranchId,
    string RequestId, string PayloadJson)
{
    public static PostgresClosingReportContextRecord FromEnvelope(ClosingReportContextEnvelope value) =>
        new(value.ContextId, value.ReportId, value.BranchId, value.RequestId, value.PayloadJson);
}
public interface IPostgresClosingReportContextSessionFactory
{
    ValueTask<IPostgresClosingReportContextSession> OpenTransactionAsync(CancellationToken token = default);
}
public interface IPostgresClosingReportContextSession : IAsyncDisposable
{
    Task<string?> ReadParentAsync(string reportId, string branchId, CancellationToken token = default);
    Task<bool> TryInsertAsync(PostgresClosingReportContextRecord context, CancellationToken token = default);
    Task<IReadOnlyList<PostgresClosingReportContextRecord>> ReadIdentityMatchesAsync(PostgresClosingReportContextRecord context, CancellationToken token = default);
    Task CommitAsync(CancellationToken token = default);
}
public sealed class PostgresClosingReportContextIngestionStore(IPostgresClosingReportContextSessionFactory sessions)
    : IClosingReportContextIngestionStore
{
    public async Task<ClosingReportContextPersistenceResult> PersistAsync(ClosingReportContextEnvelope context, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context); token.ThrowIfCancellationRequested();
        try
        {
            await using var session = await sessions.OpenTransactionAsync(token);
            var parent = await session.ReadParentAsync(context.ReportId, context.BranchId, token);
            if (parent is null) { return ClosingReportContextPersistenceResult.ParentMissing; }
            try { if (!context.MatchesParent(parent)) { return ClosingReportContextPersistenceResult.Conflict; } }
            catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or OverflowException)
            {
                throw new ClosingReportStoreUnavailableException(error);
            }
            var record = PostgresClosingReportContextRecord.FromEnvelope(context);
            if (await session.TryInsertAsync(record, token))
            {
                await session.CommitAsync(token);
                return ClosingReportContextPersistenceResult.Accepted;
            }
            var matches = await session.ReadIdentityMatchesAsync(record, token);
            if (matches.Count == 0) { throw new InvalidOperationException("Conflicting lifecycle context was not found."); }
            if (matches.Count != 1 || matches[0] != record) { return ClosingReportContextPersistenceResult.Conflict; }
            await session.CommitAsync(token);
            return ClosingReportContextPersistenceResult.AlreadyAccepted;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (NpgsqlException error) { throw new ClosingReportStoreUnavailableException(error); }
        catch (TimeoutException error) { throw new ClosingReportStoreUnavailableException(error); }
    }
}
public static class PostgresClosingReportContextSessionContract
{
    public const string ReadParentSql = """
        SELECT payload_json FROM public.closing_reports
        WHERE report_id=@report_id AND branch_id=@branch_id FOR KEY SHARE;
        """;
    public const string TryInsertSql = """
        INSERT INTO public.closing_report_contexts(context_id,report_id,branch_id,request_id,payload_json)
        VALUES (@context_id,@report_id,@branch_id,@request_id,@payload_json) ON CONFLICT DO NOTHING;
        """;
    public const string ReadIdentityMatchesSql = """
        SELECT context_id,report_id,branch_id,request_id,payload_json FROM public.closing_report_contexts
        WHERE context_id=@context_id OR report_id=@report_id ORDER BY context_id;
        """;
}
