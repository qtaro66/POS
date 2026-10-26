namespace Pos.Central.Persistence;

public sealed record PostgresAppliedMigration(
    int Version,
    string Name);

public sealed class PostgresCentralMigrationDriftException : Exception
{
    public PostgresCentralMigrationDriftException(string message)
        : base(message)
    {
    }
}

public static class PostgresCentralMigrationPlanner
{
    public static IReadOnlyList<PostgresCentralMigration> Plan(
        IReadOnlyCollection<PostgresAppliedMigration> appliedMigrations)
    {
        ArgumentNullException.ThrowIfNull(appliedMigrations);
        if (appliedMigrations.Any(migration => migration is null))
        {
            throw Drift("Applied migration history cannot contain null entries.");
        }

        var applied = appliedMigrations
            .OrderBy(migration => migration.Version)
            .ToArray();
        ValidateAppliedHistory(applied);

        var expected = PostgresCentralMigrationCatalog.GetMigrations();
        if (applied.Length > expected.Count)
        {
            throw Drift(
                "The database contains a migration newer than this application.");
        }

        for (var index = 0; index < applied.Length; index++)
        {
            var actualMigration = applied[index];
            var expectedMigration = expected[index];
            if (actualMigration.Version != expectedMigration.Version ||
                !string.Equals(
                    actualMigration.Name,
                    expectedMigration.Name,
                    StringComparison.Ordinal))
            {
                throw Drift(
                    "The applied migration history does not match the " +
                    "application migration catalog.");
            }
        }

        return Array.AsReadOnly(expected.Skip(applied.Length).ToArray());
    }

    private static void ValidateAppliedHistory(
        IReadOnlyCollection<PostgresAppliedMigration> applied)
    {
        if (applied.Any(migration => migration.Version < 1))
        {
            throw Drift("Applied migration versions must be positive.");
        }

        if (applied.Any(migration => string.IsNullOrWhiteSpace(migration.Name)))
        {
            throw Drift("Applied migration names cannot be empty.");
        }

        if (applied.Select(migration => migration.Version).Distinct().Count() !=
            applied.Count)
        {
            throw Drift("Applied migration versions must be unique.");
        }

        if (applied.Select(migration => migration.Name).Distinct(
                StringComparer.Ordinal).Count() != applied.Count)
        {
            throw Drift("Applied migration names must be unique.");
        }
    }

    private static PostgresCentralMigrationDriftException Drift(
        string message) => new(message);
}
