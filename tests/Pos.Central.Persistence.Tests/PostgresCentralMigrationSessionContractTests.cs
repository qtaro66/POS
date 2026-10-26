using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCentralMigrationSessionContractTests
{
    [Fact]
    public void Advisory_lock_key_is_stable_and_positive()
    {
        Assert.Equal(
            0x4E504F5343454E54L,
            PostgresCentralMigrationSessionContract.AdvisoryLockKey);
        Assert.True(PostgresCentralMigrationSessionContract.AdvisoryLockKey > 0);
    }

    [Fact]
    public void Advisory_lock_is_transaction_scoped_and_parameterized()
    {
        Assert.Equal(
            "lock_key",
            PostgresCentralMigrationSessionContract.AdvisoryLockParameter);
        Assert.Equal(
            "SELECT pg_advisory_xact_lock(@lock_key);",
            PostgresCentralMigrationSessionContract.AcquireAdvisoryLockSql);
        Assert.DoesNotContain(
            PostgresCentralMigrationSessionContract.AdvisoryLockKey.ToString(),
            PostgresCentralMigrationSessionContract.AcquireAdvisoryLockSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_table_probe_is_parameterized()
    {
        Assert.Equal(
            "public.central_schema_migrations",
            PostgresCentralMigrationSessionContract.QualifiedMigrationTable);
        Assert.Equal(
            "qualified_table",
            PostgresCentralMigrationSessionContract.QualifiedTableParameter);
        Assert.Equal(
            "SELECT to_regclass(@qualified_table) IS NOT NULL;",
            PostgresCentralMigrationSessionContract.MigrationTableExistsSql);
    }

    [Fact]
    public void Applied_history_read_is_schema_qualified_and_ordered()
    {
        var sql = PostgresCentralMigrationSessionContract
            .ReadAppliedMigrationsSql;

        Assert.Contains(
            "FROM public.central_schema_migrations",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY version ASC",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Session_contract_contains_read_only_control_statements()
    {
        var sql = string.Join(
            '\n',
            PostgresCentralMigrationSessionContract.AcquireAdvisoryLockSql,
            PostgresCentralMigrationSessionContract.MigrationTableExistsSql,
            PostgresCentralMigrationSessionContract.ReadAppliedMigrationsSql);

        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", sql, StringComparison.OrdinalIgnoreCase);
    }
}
