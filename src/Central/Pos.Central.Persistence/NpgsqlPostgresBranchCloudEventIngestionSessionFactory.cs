using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Pos.Central.Persistence;

public sealed class NpgsqlPostgresBranchCloudEventIngestionSessionFactory :
    IPostgresBranchCloudEventIngestionSessionFactory
{
    private readonly PostgresCentralConnectionFactory _connectionFactory;

    public NpgsqlPostgresBranchCloudEventIngestionSessionFactory(
        PostgresCentralConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async ValueTask<IPostgresBranchCloudEventIngestionSession>
        OpenTransactionAsync(
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
                    // Preserve the connection or transaction failure.
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
                    // Preserve the connection or transaction failure.
                }
            }

            throw;
        }
    }

    private sealed class Session(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction) :
        IPostgresBranchCloudEventIngestionSession
    {
        private bool _committed;
        private bool _disposed;

        public async Task<bool> TryInsertAsync(
            PostgresBranchCloudEventRecord cloudEvent,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            ArgumentNullException.ThrowIfNull(cloudEvent);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                PostgresBranchCloudEventIngestionSessionContract.TryInsertSql;
            AddEventParameters(command, cloudEvent);
            var affectedRows = await command.ExecuteNonQueryAsync(
                cancellationToken);
            return affectedRows switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidOperationException(
                    "PostgreSQL inserted an unexpected number of Cloud events.")
            };
        }

        public async Task<IReadOnlyList<PostgresBranchCloudEventRecord>>
            ReadIdentityMatchesAsync(
                string eventId,
                string branchId,
                long branchSequence,
                CancellationToken cancellationToken = default)
        {
            EnsureActive();
            eventId = RequireIdentifier(eventId, nameof(eventId));
            branchId = RequireIdentifier(branchId, nameof(branchId));
            if (branchSequence < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(branchSequence));
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = PostgresBranchCloudEventIngestionSessionContract
                .ReadIdentityMatchesSql;
            AddIdentityParameters(
                command,
                eventId,
                branchId,
                branchSequence);

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess,
                cancellationToken);
            var matches = new List<PostgresBranchCloudEventRecord>();
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(PostgresBranchCloudEventRecord.FromStorage(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }

            return matches.AsReadOnly();
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

        private static void AddEventParameters(
            NpgsqlCommand command,
            PostgresBranchCloudEventRecord cloudEvent)
        {
            AddIdentityParameters(
                command,
                cloudEvent.EventId,
                cloudEvent.BranchId,
                cloudEvent.BranchSequence);
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .RequestIdParameter,
                NpgsqlDbType.Text,
                cloudEvent.RequestId);
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .TableSessionIdParameter,
                NpgsqlDbType.Text,
                cloudEvent.TableSessionId);
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .ActionParameter,
                NpgsqlDbType.Text,
                cloudEvent.Action.ToString());
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .TableVersionParameter,
                NpgsqlDbType.Bigint,
                cloudEvent.TableVersion);
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .OccurredAtParameter,
                NpgsqlDbType.TimestampTz,
                cloudEvent.OccurredAt);
        }

        private static void AddIdentityParameters(
            NpgsqlCommand command,
            string eventId,
            string branchId,
            long branchSequence)
        {
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .EventIdParameter,
                NpgsqlDbType.Text,
                eventId);
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .BranchIdParameter,
                NpgsqlDbType.Text,
                branchId);
            command.Parameters.AddWithValue(
                PostgresBranchCloudEventIngestionSessionContract
                    .BranchSequenceParameter,
                NpgsqlDbType.Bigint,
                branchSequence);
        }

        private static string RequireIdentifier(
            string value,
            string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(
                    "The identifier cannot be empty.",
                    parameterName)
                : value.Trim();

        private void EnsureActive()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL ingestion transaction is already committed.");
            }
        }
    }
}
