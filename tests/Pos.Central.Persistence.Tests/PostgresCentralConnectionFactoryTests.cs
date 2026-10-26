using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCentralConnectionFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_connection_string_is_rejected(string? connectionString)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PostgresCentralConnectionFactory(connectionString!));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void Invalid_connection_string_is_rejected_without_echoing_secret()
    {
        const string secret = "do-not-echo-this-secret";

        var exception = Assert.Throws<ArgumentException>(() =>
            new PostgresCentralConnectionFactory(
                $"Host=central.invalid;Password={secret};Unknown Option=value"));

        Assert.Equal("connectionString", exception.ParamName);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Database=pos")]
    [InlineData("Host=central.invalid")]
    public void Required_destination_fields_are_enforced(string connectionString)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PostgresCentralConnectionFactory(connectionString));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public async Task Valid_configuration_builds_without_network_access()
    {
        await using var factory = new PostgresCentralConnectionFactory(
            "Host=central.invalid;Database=pos;Username=pos_app");

        Assert.Equal(
            PostgresCentralConnectionFactory.DefaultApplicationName,
            factory.ApplicationName);
    }

    [Fact]
    public async Task Explicit_application_name_is_preserved()
    {
        await using var factory = new PostgresCentralConnectionFactory(
            "Host=central.invalid;Database=pos;Application Name=Central.Tests");

        Assert.Equal("Central.Tests", factory.ApplicationName);
    }
}
