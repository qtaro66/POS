using Pos.Application.Synchronization;
using Pos.Central.Persistence;
using Pos.Domain.TableSessions;

namespace Pos.Central.Persistence.Tests;

public sealed class NpgsqlPostgresBranchCloudEventIngestionSessionFactoryTests
{
    [Fact]
    public async Task Construction_does_not_open_a_database_connection()
    {
        await using var connectionFactory =
            new PostgresCentralConnectionFactory(
                "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1");

        var sessionFactory =
            new NpgsqlPostgresBranchCloudEventIngestionSessionFactory(
                connectionFactory);

        Assert.NotNull(sessionFactory);
    }

    [Fact]
    public void Missing_connection_factory_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NpgsqlPostgresBranchCloudEventIngestionSessionFactory(null!));
    }

    [Fact]
    public void Envelope_timestamp_is_normalized_to_postgres_precision()
    {
        var original = new DateTimeOffset(
            638971794001234567,
            TimeSpan.Zero);
        var envelope = BranchCloudOutboxEventEnvelope.Create(
            "EVENT-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            TableSessionId.From("SESSION-01"),
            TableSessionAction.SubmitOrder,
            3,
            original);

        var record = PostgresBranchCloudEventRecord.FromEnvelope(envelope);

        Assert.Equal(0, record.OccurredAt.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(
            original.Ticks - original.Ticks % TimeSpan.TicksPerMicrosecond,
            record.OccurredAt.Ticks);
        Assert.Equal(TimeSpan.Zero, record.OccurredAt.Offset);
    }

    [Fact]
    public void Storage_action_is_case_sensitive_and_defined()
    {
        var valid = FromStorage(
            nameof(TableSessionAction.SubmitOrder),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(TableSessionAction.SubmitOrder, valid.Action);
        Assert.Throws<InvalidOperationException>(() =>
            FromStorage("submitorder", DateTimeOffset.UnixEpoch));
        Assert.Throws<InvalidOperationException>(() =>
            FromStorage("UnknownAction", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Storage_timestamp_must_be_utc()
    {
        var nonUtc = new DateTimeOffset(
            2026,
            10,
            27,
            10,
            30,
            0,
            TimeSpan.FromHours(7));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            FromStorage(nameof(TableSessionAction.SubmitOrder), nonUtc));

        Assert.Contains("not UTC", exception.Message);
    }

    private static PostgresBranchCloudEventRecord FromStorage(
        string action,
        DateTimeOffset occurredAt) =>
        PostgresBranchCloudEventRecord.FromStorage(
            "EVENT-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            "SESSION-01",
            action,
            3,
            occurredAt);
}
