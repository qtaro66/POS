namespace Pos.Central.Persistence;

public static class PostgresBranchCloudEventIngestionSessionContract
{
    public const string EventIdParameter = "event_id";

    public const string BranchIdParameter = "branch_id";

    public const string BranchSequenceParameter = "branch_sequence";

    public const string RequestIdParameter = "request_id";

    public const string TableSessionIdParameter = "table_session_id";

    public const string ActionParameter = "action";

    public const string TableVersionParameter = "table_version";

    public const string OccurredAtParameter = "occurred_at";

    public const string AcquireBranchSequenceLockSql = """
        SELECT pg_advisory_xact_lock(hashtextextended(@branch_id, 0));
        """;

    public const string ReadNextBranchSequenceSql = """
        SELECT COALESCE(MAX(branch_sequence), 0) + 1
        FROM public.branch_cloud_events
        WHERE branch_id = @branch_id;
        """;

    public const string TryInsertSql = """
        INSERT INTO public.branch_cloud_events (
            event_id,
            branch_id,
            branch_sequence,
            request_id,
            table_session_id,
            action,
            table_version,
            occurred_at
        )
        VALUES (
            @event_id,
            @branch_id,
            @branch_sequence,
            @request_id,
            @table_session_id,
            @action,
            @table_version,
            @occurred_at
        )
        ON CONFLICT DO NOTHING;
        """;

    public const string ReadIdentityMatchesSql = """
        SELECT
            event_id,
            branch_id,
            branch_sequence,
            request_id,
            table_session_id,
            action,
            table_version,
            occurred_at
        FROM public.branch_cloud_events
        WHERE event_id = @event_id
           OR (branch_id = @branch_id AND branch_sequence = @branch_sequence)
        ORDER BY event_id ASC, branch_id ASC, branch_sequence ASC;
        """;
}
