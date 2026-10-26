using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class NpgsqlPostgresCentralMigrationSessionFactoryTests
{
    [Fact]
    public async Task Construction_does_not_open_a_database_connection()
    {
        await using var connectionFactory =
            new PostgresCentralConnectionFactory(
                "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1");

        var sessionFactory =
            new NpgsqlPostgresCentralMigrationSessionFactory(connectionFactory);

        Assert.NotNull(sessionFactory);
    }

    [Fact]
    public void Missing_connection_factory_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NpgsqlPostgresCentralMigrationSessionFactory(null!));
    }
}
