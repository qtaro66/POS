using System.Reflection;

namespace Pos.Central.Persistence;

public sealed record PostgresCentralMigration(
    int Version,
    string Name,
    string Sql);

public static class PostgresCentralMigrationCatalog
{
    public const int CurrentVersion = 1;

    private const string Version1Name = "0001_branch_cloud_events";

    private static readonly Lazy<IReadOnlyList<PostgresCentralMigration>>
        Migrations = new(CreateMigrations);

    public static IReadOnlyList<PostgresCentralMigration> GetMigrations() =>
        Migrations.Value;

    private static IReadOnlyList<PostgresCentralMigration> CreateMigrations() =>
        Array.AsReadOnly(
        [
            new PostgresCentralMigration(
                CurrentVersion,
                Version1Name,
                ReadEmbeddedSql(
                    "Pos.Central.Persistence.Migrations." +
                    $"{Version1Name}.sql"))
        ]);

    private static string ReadEmbeddedSql(string resourceName)
    {
        using var stream = typeof(PostgresCentralMigrationCatalog)
            .Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded PostgreSQL migration '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
