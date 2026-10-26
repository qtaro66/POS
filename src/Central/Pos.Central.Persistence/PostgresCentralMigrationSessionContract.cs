namespace Pos.Central.Persistence;

public static class PostgresCentralMigrationSessionContract
{
    public const long AdvisoryLockKey = 0x4E504F5343454E54L;

    public const string AdvisoryLockParameter = "lock_key";

    public const string QualifiedMigrationTable =
        "public.central_schema_migrations";

    public const string QualifiedTableParameter = "qualified_table";

    public const string AcquireAdvisoryLockSql =
        "SELECT pg_advisory_xact_lock(@lock_key);";

    public const string MigrationTableExistsSql =
        "SELECT to_regclass(@qualified_table) IS NOT NULL;";

    public const string ReadAppliedMigrationsSql = """
        SELECT version, name
        FROM public.central_schema_migrations
        ORDER BY version ASC;
        """;
}
