using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresBranchCloudEventIngestionSessionContractTests
{
    [Fact]
    public void Parameter_names_are_stable_and_canonical()
    {
        Assert.Equal(
            [
                "event_id",
                "branch_id",
                "branch_sequence",
                "request_id",
                "table_session_id",
                "action",
                "table_version",
                "occurred_at"
            ],
            Parameters());
    }

    [Fact]
    public void Insert_is_schema_qualified_parameterized_and_conflict_safe()
    {
        var sql = PostgresBranchCloudEventIngestionSessionContract.TryInsertSql;

        Assert.Contains(
            "INSERT INTO public.branch_cloud_events",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON CONFLICT DO NOTHING",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("received_at", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RETURNING", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var parameter in Parameters())
        {
            Assert.Contains($"@{parameter}", sql, StringComparison.Ordinal);
            Assert.Equal(1, Count(sql, $"@{parameter}"));
        }
    }

    [Fact]
    public void Identity_read_checks_both_unique_keys_and_complete_payload()
    {
        var sql = PostgresBranchCloudEventIngestionSessionContract
            .ReadIdentityMatchesSql;

        Assert.Contains(
            "FROM public.branch_cloud_events",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE event_id = @event_id",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "branch_id = @branch_id AND branch_sequence = @branch_sequence",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY event_id ASC, branch_id ASC, branch_sequence ASC",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);

        foreach (var column in Parameters())
        {
            Assert.Contains(column, sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("received_at", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sequence_read_serializes_each_branch_before_selecting_next_value()
    {
        var lockSql = PostgresBranchCloudEventIngestionSessionContract
            .AcquireBranchSequenceLockSql;
        var readSql = PostgresBranchCloudEventIngestionSessionContract
            .ReadNextBranchSequenceSql;

        Assert.Contains(
            "pg_advisory_xact_lock(hashtextextended(@branch_id, 0))",
            lockSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "COALESCE(MAX(branch_sequence), 0) + 1",
            readSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "FROM public.branch_cloud_events",
            readSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE branch_id = @branch_id",
            readSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_never_updates_or_deletes_persisted_events()
    {
        var sql = string.Join(
            '\n',
            PostgresBranchCloudEventIngestionSessionContract.TryInsertSql,
            PostgresBranchCloudEventIngestionSessionContract
                .AcquireBranchSequenceLockSql,
            PostgresBranchCloudEventIngestionSessionContract
                .ReadNextBranchSequenceSql,
            PostgresBranchCloudEventIngestionSessionContract
                .ReadIdentityMatchesSql);

        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Parameters() =>
    [
        PostgresBranchCloudEventIngestionSessionContract.EventIdParameter,
        PostgresBranchCloudEventIngestionSessionContract.BranchIdParameter,
        PostgresBranchCloudEventIngestionSessionContract.BranchSequenceParameter,
        PostgresBranchCloudEventIngestionSessionContract.RequestIdParameter,
        PostgresBranchCloudEventIngestionSessionContract.TableSessionIdParameter,
        PostgresBranchCloudEventIngestionSessionContract.ActionParameter,
        PostgresBranchCloudEventIngestionSessionContract.TableVersionParameter,
        PostgresBranchCloudEventIngestionSessionContract.OccurredAtParameter
    ];

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(
            value,
            offset,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
