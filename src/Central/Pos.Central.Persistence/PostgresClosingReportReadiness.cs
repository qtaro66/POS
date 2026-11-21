using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pos.Application.Synchronization;

namespace Pos.Central.Persistence;

public interface IPostgresClosingReportReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken token = default);
}

public sealed class NpgsqlPostgresClosingReportReadinessProbe(PostgresCentralConnectionFactory connections)
    : IPostgresClosingReportReadinessProbe
{
    public const string InspectSql = """
        SELECT
            (SELECT COALESCE(MAX(version),0) FROM public.central_schema_migrations) = @version
            AND NOT EXISTS (
                SELECT 1 FROM (VALUES
                    ('report_id','text'),('branch_id','text'),('request_id','text'),
                    ('store_session_id','text'),('shift_id','text'),('operation','text'),
                    ('business_date','date'),('occurred_at','timestamptz'),('payload_json','text'),('received_at','timestamptz')
                ) AS required(column_name,udt_name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM information_schema.columns actual
                    WHERE actual.table_schema='public' AND actual.table_name='closing_reports'
                        AND actual.column_name=required.column_name AND actual.udt_name=required.udt_name
                        AND actual.is_nullable='NO'
                )
            )
            AND NOT EXISTS (
                SELECT 1 FROM (VALUES
                    ('p',ARRAY['report_id']),
                    ('u',ARRAY['branch_id','request_id']),
                    ('u',ARRAY['branch_id','store_session_id','shift_id'])
                ) AS required(kind,columns)
                WHERE NOT EXISTS (
                    SELECT 1 FROM pg_constraint c
                    WHERE c.conrelid=to_regclass('public.closing_reports')
                        AND c.contype::text=required.kind AND c.convalidated AND NOT c.condeferrable
                        AND ARRAY(
                            SELECT a.attname::text FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum,position)
                            JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum
                            ORDER BY k.position
                        )=required.columns
                )
            );
        """;

    public async Task<bool> IsReadyAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await connections.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = InspectSql;
            command.Parameters.AddWithValue("version", PostgresCentralMigrationCatalog.CurrentVersion);
            return await command.ExecuteScalarAsync(token) is true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (NpgsqlException) { return false; }
        catch (TimeoutException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}

public static class PostgresClosingReportIngestionServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresClosingReportIngestion(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString)) { throw new ArgumentException("The Central PostgreSQL connection string cannot be empty.", nameof(connectionString)); }
        services.TryAddSingleton(_ => new PostgresCentralConnectionFactory(connectionString));
        services.TryAddSingleton<IPostgresClosingReportSessionFactory, NpgsqlPostgresClosingReportSessionFactory>();
        services.TryAddSingleton<IClosingReportIngestionStore, PostgresClosingReportIngestionStore>();
        services.TryAddSingleton<IPostgresClosingReportReadinessProbe, NpgsqlPostgresClosingReportReadinessProbe>();
        return services;
    }
}
