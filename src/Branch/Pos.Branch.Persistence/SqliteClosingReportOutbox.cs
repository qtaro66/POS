namespace Pos.Branch.Infrastructure.Sqlite;

public enum ClosingReportDeliveryFailure { TransportUnavailable, CentralRejected, AcknowledgmentUnavailable }
public sealed record ClosingReportLease(string BranchId, string ReportId, string RequestId, string Operation,
    string PayloadJson, string LeaseId, string LeaseOwner, DateTimeOffset LeaseExpiresAt, int AttemptCount);
public sealed record ClosingReportOutboxStatus(string BranchId, int PendingCount, int LeasedCount, int DeliveredCount,
    DateTimeOffset? LastDeliveredAt);

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<ClosingReportLease?> ClaimClosingReportAsync(string branchId, string workerId,
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ClaimClosingReportAsync(branchId, workerId, now, leaseDuration, cancellation), token);

    public Task<bool> ConfirmClosingReportAsync(ClosingReportLease lease, DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).FinishClosingReportAsync(lease, now, null, null, cancellation), token);

    public Task<bool> RetryClosingReportAsync(ClosingReportLease lease, DateTimeOffset now, TimeSpan retryDelay,
        ClosingReportDeliveryFailure failure, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).FinishClosingReportAsync(lease, now, retryDelay, failure, cancellation), token);

    public Task<ClosingReportOutboxStatus> ReadClosingReportOutboxAsync(string branchId, DateTimeOffset now,
        CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadClosingReportOutboxAsync(branchId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<ClosingReportLease?> ClaimClosingReportAsync(string branchId, string workerId,
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        RequireClosingReportUtc(now);
        if (leaseDuration <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(leaseDuration)); }
        var expiresAt = now.Add(leaseDuration);
        using var read = CreateCommand("""
            SELECT report_id,request_id,operation,payload_json,attempt_count
            FROM closing_report_outbox
            WHERE branch_id=$branch AND delivered_at IS NULL AND julianday(available_at) <= julianday($now)
                AND (lease_expires_at IS NULL OR lease_expires_at <= $now)
            ORDER BY sequence LIMIT 1;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$now", FormatTimestamp(now));
        string reportId, requestId, operation, payload;
        int attempts;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) { return null; }
            reportId = reader.GetString(0);
            requestId = reader.GetString(1);
            operation = reader.GetString(2);
            payload = reader.GetString(3);
            attempts = checked(reader.GetInt32(4) + 1);
        }
        var leaseId = Guid.NewGuid().ToString("N");
        using var write = CreateCommand("""
            UPDATE closing_report_outbox SET lease_id=$lease,lease_owner=$owner,
                lease_expires_at=$expires,attempt_count=$attempts
            WHERE branch_id=$branch AND report_id=$report;
            """);
        write.Parameters.AddWithValue("$lease", leaseId);
        write.Parameters.AddWithValue("$owner", workerId);
        write.Parameters.AddWithValue("$expires", FormatTimestamp(expiresAt));
        write.Parameters.AddWithValue("$attempts", attempts);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$report", reportId);
        await write.ExecuteNonQueryAsync(token);
        return new(branchId, reportId, requestId, operation, payload, leaseId, workerId, expiresAt, attempts);
    }

    internal async Task<bool> FinishClosingReportAsync(ClosingReportLease lease, DateTimeOffset now,
        TimeSpan? retryDelay, ClosingReportDeliveryFailure? failure, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        RequireClosingReportUtc(now);
        if (failure is not null && !Enum.IsDefined(failure.Value)) { throw new ArgumentOutOfRangeException(nameof(failure)); }
        if (retryDelay is not null && retryDelay <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(retryDelay)); }
        var next = retryDelay is null ? now : now.Add(retryDelay.Value);
        using var write = CreateCommand("""
            UPDATE closing_report_outbox
            SET delivered_at=$delivered,available_at=$next,last_failure=$failure,
                lease_id=NULL,lease_owner=NULL,lease_expires_at=NULL
            WHERE branch_id=$branch AND report_id=$report AND lease_id=$lease AND lease_owner=$owner
                AND delivered_at IS NULL AND lease_expires_at > $now;
            """);
        write.Parameters.AddWithValue("$delivered", failure is null ? FormatTimestamp(now) : DBNull.Value);
        write.Parameters.AddWithValue("$next", FormatTimestamp(next));
        write.Parameters.AddWithValue("$failure", failure is null ? DBNull.Value : failure.Value.ToString());
        write.Parameters.AddWithValue("$branch", lease.BranchId);
        write.Parameters.AddWithValue("$report", lease.ReportId);
        write.Parameters.AddWithValue("$lease", lease.LeaseId);
        write.Parameters.AddWithValue("$owner", lease.LeaseOwner);
        write.Parameters.AddWithValue("$now", FormatTimestamp(now));
        return await write.ExecuteNonQueryAsync(token) == 1;
    }

    internal async Task<ClosingReportOutboxStatus> ReadClosingReportOutboxAsync(string branchId,
        DateTimeOffset now, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        RequireClosingReportUtc(now);
        using var read = CreateCommand("""
            SELECT COUNT(*) FILTER (WHERE delivered_at IS NULL),
                COUNT(*) FILTER (WHERE delivered_at IS NULL AND lease_expires_at > $now),
                COUNT(*) FILTER (WHERE delivered_at IS NOT NULL),MAX(delivered_at)
            FROM closing_report_outbox WHERE branch_id=$branch;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$now", FormatTimestamp(now));
        await using var reader = await read.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return new(branchId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)));
    }

    private static void RequireClosingReportUtc(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero) { throw new ArgumentException("Closing report timestamps must use UTC.", nameof(now)); }
    }
}
