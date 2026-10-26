using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Pos.Central.Persistence;

public sealed class NpgsqlPostgresCentralMigrationSessionFactory :
    IPostgresCentralMigrationSessionFactory
{
    private readonly PostgresCentralConnectionFactory _connectionFactory;

    public NpgsqlPostgresCentralMigrationSessionFactory(
        PostgresCentralConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async ValueTask<IPostgresCentralMigrationSession>
        OpenLockedTransactionAsync(
            CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? transaction = null;
        try
        {
            connection = await _connectionFactory.OpenConnectionAsync(
                cancellationToken);
            transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = PostgresCentralMigrationSessionContract
                .AcquireAdvisoryLockSql;
            command.Parameters.AddWithValue(
                PostgresCentralMigrationSessionContract.AdvisoryLockParameter,
                NpgsqlDbType.Bigint,
                PostgresCentralMigrationSessionContract.AdvisoryLockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);

            return new Session(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch
                {
                    // Preserve the connection, transaction, or lock failure.
                }
            }

            if (connection is not null)
            {
                try
                {
                    await connection.DisposeAsync();
                }
                catch
                {
                    // Preserve the connection, transaction, or lock failure.
                }
            }

            throw;
        }
    }

    private sealed class Session(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction) : IPostgresCentralMigrationSession
    {
        private bool _committed;
        private bool _disposed;

        public async Task<IReadOnlyCollection<PostgresAppliedMigration>>
            ReadAppliedMigrationsAsync(
                CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (!await MigrationTableExistsAsync(cancellationToken))
            {
                return Array.Empty<PostgresAppliedMigration>();
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = PostgresCentralMigrationSessionContract
                .ReadAppliedMigrationsSql;
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess,
                cancellationToken);
            var migrations = new List<PostgresAppliedMigration>();
            while (await reader.ReadAsync(cancellationToken))
            {
                migrations.Add(new PostgresAppliedMigration(
                    reader.GetInt32(0),
                    reader.GetString(1)));
            }

            return migrations.AsReadOnly();
        }

        public async Task ApplyMigrationAsync(
            PostgresCentralMigration migration,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            ArgumentNullException.ThrowIfNull(migration);
            EnsureCatalogMigration(migration);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task CommitAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            await transaction.CommitAsync(cancellationToken);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (!_committed)
                {
                    await transaction.RollbackAsync();
                }
            }
            finally
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                finally
                {
                    await connection.DisposeAsync();
                }
            }
        }

        private async Task<bool> MigrationTableExistsAsync(
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = PostgresCentralMigrationSessionContract
                .MigrationTableExistsSql;
            command.Parameters.AddWithValue(
                PostgresCentralMigrationSessionContract.QualifiedTableParameter,
                NpgsqlDbType.Text,
                PostgresCentralMigrationSessionContract.QualifiedMigrationTable);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool exists
                ? exists
                : throw new InvalidOperationException(
                    "PostgreSQL returned an invalid migration-table probe result.");
        }

        private static void EnsureCatalogMigration(
            PostgresCentralMigration migration)
        {
            var expected = PostgresCentralMigrationCatalog
                .GetMigrations()
                .SingleOrDefault(candidate =>
                    candidate.Version == migration.Version);
            if (expected is null ||
                !string.Equals(
                    expected.Name,
                    migration.Name,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    expected.Sql,
                    migration.Sql,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Only an exact embedded catalog migration may be applied.",
                    nameof(migration));
            }
        }

        private void EnsureActive()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL migration transaction is already committed.");
            }
        }
    }
}
