using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pos.Application.Synchronization;

namespace Pos.Central.Persistence;

public interface IPostgresClosingReportContextReadinessProbe { Task<bool> IsReadyAsync(CancellationToken token = default); }
public sealed class NpgsqlPostgresClosingReportContextReadinessProbe(PostgresCentralConnectionFactory connections)
    : IPostgresClosingReportContextReadinessProbe
{
    public const string InspectSql = """
        SELECT (SELECT COALESCE(MAX(version),0) FROM public.central_schema_migrations)=@version
        AND NOT EXISTS (
            SELECT 1 FROM (VALUES ('context_id','text'),('report_id','text'),('branch_id','text'),
                ('request_id','text'),('payload_json','text'),('received_at','timestamptz')) AS required(name,type)
            WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns actual
                WHERE actual.table_schema='public' AND actual.table_name='closing_report_contexts'
                    AND actual.column_name=required.name AND actual.udt_name=required.type AND actual.is_nullable='NO'))
        AND NOT EXISTS (
            SELECT 1 FROM (VALUES ('p',ARRAY['context_id']),('u',ARRAY['report_id'])) AS required(kind,columns)
            WHERE NOT EXISTS (SELECT 1 FROM pg_constraint c
                WHERE c.conrelid=to_regclass('public.closing_report_contexts') AND c.contype::text=required.kind
                    AND c.convalidated AND NOT c.condeferrable
                    AND ARRAY(SELECT a.attname::text FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum,position)
                        JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum ORDER BY k.position)=required.columns))
        AND EXISTS (SELECT 1 FROM pg_constraint c
            WHERE c.conrelid=to_regclass('public.closing_report_contexts') AND c.confrelid=to_regclass('public.closing_reports')
                AND c.contype='f' AND c.convalidated AND NOT c.condeferrable
                AND ARRAY(SELECT a.attname::text FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum,position)
                    JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum ORDER BY k.position)=ARRAY['report_id']
                AND ARRAY(SELECT a.attname::text FROM unnest(c.confkey) WITH ORDINALITY AS k(attnum,position)
                    JOIN pg_attribute a ON a.attrelid=c.confrelid AND a.attnum=k.attnum ORDER BY k.position)=ARRAY['report_id']);
        """;
    public async Task<bool> IsReadyAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await connections.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand(); command.CommandText = InspectSql;
            command.Parameters.AddWithValue("version", PostgresCentralMigrationCatalog.CurrentVersion);
            return await command.ExecuteScalarAsync(token) is true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (NpgsqlException) { return false; }
        catch (TimeoutException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
public static class PostgresClosingReportContextComposition
{
    public static IServiceCollection AddPostgresClosingReportContexts(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(_ => new PostgresCentralConnectionFactory(connectionString));
        services.TryAddSingleton<IPostgresClosingReportContextSessionFactory, NpgsqlPostgresClosingReportContextSessionFactory>();
        services.TryAddSingleton<IClosingReportContextIngestionStore, PostgresClosingReportContextIngestionStore>();
        services.TryAddSingleton<IPostgresClosingReportContextReadinessProbe, NpgsqlPostgresClosingReportContextReadinessProbe>();
        return services;
    }
}
