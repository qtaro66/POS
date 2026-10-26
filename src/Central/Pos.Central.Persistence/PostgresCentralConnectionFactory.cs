using Npgsql;

namespace Pos.Central.Persistence;

public sealed class PostgresCentralConnectionFactory : IAsyncDisposable
{
    public const string DefaultApplicationName = "Niceloop.POS.Central";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresCentralConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "The Central PostgreSQL connection string cannot be empty.",
                nameof(connectionString));
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                "The Central PostgreSQL connection string is invalid.",
                nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            throw new ArgumentException(
                "The Central PostgreSQL connection string must specify Host.",
                nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new ArgumentException(
                "The Central PostgreSQL connection string must specify Database.",
                nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = DefaultApplicationName;
        }

        ApplicationName = builder.ApplicationName;
        _dataSource = new NpgsqlDataSourceBuilder(builder.ConnectionString)
            .Build();
    }

    public string ApplicationName { get; }

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default) =>
        _dataSource.OpenConnectionAsync(cancellationToken);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
