namespace Pos.Branch.Persistence.Tests;

internal static class LegacySchemaFixture
{
    // Legacy fixtures start from today's initialized database. Remove future
    // reprint structures before restoring their older schema version marker.
    internal static string WithoutKitchenReprints(string sql) => """
        DROP TRIGGER IF EXISTS print_jobs_kitchen_reprint_insert_guard;
        DROP TRIGGER IF EXISTS print_jobs_kitchen_reprint_update_guard;
        DROP TABLE IF EXISTS kitchen_reprint_audit;
        DROP TABLE IF EXISTS kitchen_reprint_requests;
        DROP TABLE IF EXISTS kitchen_reprint_snapshots;

        """ + sql;
}
