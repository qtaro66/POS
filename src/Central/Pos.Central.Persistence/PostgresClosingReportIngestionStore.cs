using Npgsql;
using Pos.Application.Synchronization;

namespace Pos.Central.Persistence;

public sealed record PostgresClosingReportRecord(string ReportId, string BranchId, string RequestId,
    string StoreSessionId, string ShiftId, string Operation, DateOnly BusinessDate, DateTimeOffset OccurredAt, string PayloadJson)
{
    public static PostgresClosingReportRecord FromEnvelope(ClosingReportEnvelope report) =>
        new(report.ReportId, report.BranchId, report.RequestId, report.StoreSessionId, report.ShiftId,
            report.Operation, report.BusinessDate,
            new DateTimeOffset(report.OccurredAt.Ticks - report.OccurredAt.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero),
            report.PayloadJson);
}

public interface IPostgresClosingReportSessionFactory
{
    ValueTask<IPostgresClosingReportSession> OpenTransactionAsync(CancellationToken token = default);
}

public interface IPostgresClosingReportSession : IAsyncDisposable
{
    Task<bool> TryInsertAsync(PostgresClosingReportRecord report, CancellationToken token = default);
    Task<IReadOnlyList<PostgresClosingReportRecord>> ReadIdentityMatchesAsync(PostgresClosingReportRecord report, CancellationToken token = default);
    Task CommitAsync(CancellationToken token = default);
}

public sealed class PostgresClosingReportIngestionStore(IPostgresClosingReportSessionFactory sessions) : IClosingReportIngestionStore
{
    public async Task<ClosingReportPersistenceResult> PersistAsync(ClosingReportEnvelope report, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        token.ThrowIfCancellationRequested();
        try
        {
            var expected = PostgresClosingReportRecord.FromEnvelope(report);
            await using var session = await sessions.OpenTransactionAsync(token)
                ?? throw new InvalidOperationException("Closing report session was not available.");
            if (await session.TryInsertAsync(expected, token))
            {
                await session.CommitAsync(token);
                return ClosingReportPersistenceResult.Accepted;
            }
            var matches = await session.ReadIdentityMatchesAsync(expected, token);
            if (matches.Count == 0) { throw new InvalidOperationException("Conflicting closing report was not found."); }
            if (matches.Count != 1 || matches[0] != expected) { return ClosingReportPersistenceResult.Conflict; }
            await session.CommitAsync(token);
            return ClosingReportPersistenceResult.AlreadyAccepted;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (NpgsqlException error) { throw new ClosingReportStoreUnavailableException(error); }
        catch (TimeoutException error) { throw new ClosingReportStoreUnavailableException(error); }
    }
}

public static class PostgresClosingReportSessionContract
{
    public const string TryInsertSql = """
        INSERT INTO public.closing_reports
            (report_id,branch_id,request_id,store_session_id,shift_id,operation,business_date,occurred_at,payload_json)
        VALUES (@report_id,@branch_id,@request_id,@store_session_id,@shift_id,@operation,@business_date,@occurred_at,@payload_json)
        ON CONFLICT DO NOTHING;
        """;
    public const string ReadIdentityMatchesSql = """
        SELECT report_id,branch_id,request_id,store_session_id,shift_id,operation,business_date,occurred_at,payload_json
        FROM public.closing_reports
        WHERE report_id=@report_id OR (branch_id=@branch_id AND request_id=@request_id)
            OR (branch_id=@branch_id AND store_session_id=@store_session_id AND shift_id=@shift_id)
        ORDER BY report_id;
        """;
}
