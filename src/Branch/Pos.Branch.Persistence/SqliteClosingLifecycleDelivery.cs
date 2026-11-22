using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<ClosingLifecycleLease?> ClaimClosingLifecycleAsync(string branchId, string workerId,
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ClaimClosingLifecycleAsync(branchId, workerId, now, leaseDuration, cancellation), token);
    public Task<bool> ConfirmClosingLifecycleAsync(ClosingLifecycleLease lease, DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).FinishClosingLifecycleAsync(lease, now, null, null, cancellation), token);
    public Task<bool> RetryClosingLifecycleAsync(ClosingLifecycleLease lease, DateTimeOffset now, TimeSpan retryDelay,
        ClosingLifecycleFailure failure, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).FinishClosingLifecycleAsync(lease, now, retryDelay, failure, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<ClosingLifecycleLease?> ClaimClosingLifecycleAsync(string branchId, string workerId,
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        RequireClosingReportUtc(now);
        if (leaseDuration <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(leaseDuration)); }
        using var read = CreateCommand("""
            SELECT context.context_id,parent.report_id,context.request_id,parent.operation,
                context.payload_json,parent.payload_json,context.attempt_count
            FROM closing_report_context_outbox context
            JOIN closing_report_outbox parent ON parent.branch_id=context.branch_id AND parent.request_id=context.request_id
            WHERE context.branch_id=$branch AND context.delivered_at IS NULL AND parent.delivered_at IS NOT NULL
                AND context.available_at <= $now AND (context.lease_expires_at IS NULL OR context.lease_expires_at <= $now)
            ORDER BY context.sequence LIMIT 1;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$now", FormatTimestamp(now));
        string contextId, reportId, requestId, operation, payload, parentPayload;
        int attempts;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) { return null; }
            contextId = reader.GetString(0); reportId = reader.GetString(1); requestId = reader.GetString(2);
            operation = reader.GetString(3); payload = reader.GetString(4); parentPayload = reader.GetString(5);
            attempts = checked(reader.GetInt32(6) + 1);
        }
        var leaseId = Guid.NewGuid().ToString("N");
        var expires = now.Add(leaseDuration);
        using var write = CreateCommand("""
            UPDATE closing_report_context_outbox SET lease_id=$lease,lease_owner=$owner,
                lease_expires_at=$expires,attempt_count=$attempts
            WHERE branch_id=$branch AND context_id=$context;
            """);
        write.Parameters.AddWithValue("$lease", leaseId); write.Parameters.AddWithValue("$owner", workerId);
        write.Parameters.AddWithValue("$expires", FormatTimestamp(expires)); write.Parameters.AddWithValue("$attempts", attempts);
        write.Parameters.AddWithValue("$branch", branchId); write.Parameters.AddWithValue("$context", contextId);
        await write.ExecuteNonQueryAsync(token);
        return new(branchId, contextId, reportId, requestId, operation, payload, parentPayload, leaseId, workerId, expires, attempts);
    }

    internal async Task<bool> FinishClosingLifecycleAsync(ClosingLifecycleLease lease, DateTimeOffset now,
        TimeSpan? retryDelay, ClosingLifecycleFailure? failure, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        RequireClosingReportUtc(now);
        if (failure is not null && !Enum.IsDefined(failure.Value)) { throw new ArgumentOutOfRangeException(nameof(failure)); }
        if (retryDelay is not null && retryDelay <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(retryDelay)); }
        var next = retryDelay is null ? now : now.Add(retryDelay.Value);
        using var write = CreateCommand("""
            UPDATE closing_report_context_outbox SET delivered_at=$delivered,available_at=$next,last_failure=$failure,
                lease_id=NULL,lease_owner=NULL,lease_expires_at=NULL
            WHERE branch_id=$branch AND context_id=$context AND lease_id=$lease AND lease_owner=$owner
                AND delivered_at IS NULL AND lease_expires_at > $now;
            """);
        write.Parameters.AddWithValue("$delivered", failure is null ? FormatTimestamp(now) : DBNull.Value);
        write.Parameters.AddWithValue("$next", FormatTimestamp(next));
        write.Parameters.AddWithValue("$failure", failure is null ? DBNull.Value : failure.Value.ToString());
        write.Parameters.AddWithValue("$branch", lease.BranchId); write.Parameters.AddWithValue("$context", lease.ContextId);
        write.Parameters.AddWithValue("$lease", lease.LeaseId); write.Parameters.AddWithValue("$owner", lease.LeaseOwner);
        write.Parameters.AddWithValue("$now", FormatTimestamp(now));
        return await write.ExecuteNonQueryAsync(token) == 1;
    }

    private async Task<ClosingLifecycleDelivery?> ReadClosingLifecycleDeliveryAsync(string branchId, string requestId,
        DateTimeOffset now, bool hasSnapshot, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT context.context_id,context.attempt_count,context.available_at,context.lease_expires_at,context.delivered_at,context.last_failure,
                'lifecycle:' || parent.report_id
            FROM closing_report_context_outbox context JOIN closing_report_outbox parent
                ON parent.branch_id=context.branch_id AND parent.request_id=context.request_id
            WHERE context.branch_id=$branch AND context.request_id=$request;
            """);
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$request", requestId);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            if (hasSnapshot) { throw new InvalidDataException("Saved lifecycle delivery is unavailable."); }
            return null;
        }
        if (!hasSnapshot || reader.GetString(0) != reader.GetString(6)) { throw new InvalidDataException("Lifecycle delivery has no consistent saved identity."); }
        var available = ReadReportTimestamp(reader.GetString(2));
        DateTimeOffset? expires = reader.IsDBNull(3) ? null : ReadReportTimestamp(reader.GetString(3));
        DateTimeOffset? delivered = reader.IsDBNull(4) ? null : ReadReportTimestamp(reader.GetString(4));
        var hasFailure = !reader.IsDBNull(5);
        var failure = hasFailure ? reader.GetString(5) : null;
        if (!Enum.TryParse<ClosingLifecycleFailure>(failure, out var known) || !Enum.IsDefined(known) || known.ToString() != failure) { failure = null; }
        var state = delivered is not null ? "Delivered" : expires > now ? "InFlight" : expires is not null ? "LeaseExpired"
            : hasFailure && available > now ? "RetryScheduled" : "Queued";
        return new(reader.GetString(0), state, reader.GetInt32(1), available, expires, delivered, failure);
    }
}
