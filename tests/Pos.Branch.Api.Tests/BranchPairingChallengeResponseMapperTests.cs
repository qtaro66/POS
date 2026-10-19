using System.Text.Json;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingChallengeResponseMapperTests
{
    [Fact]
    public void Issued_challenge_maps_to_versioned_phase_zero_response()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var expiresAt = DateTimeOffset.Parse("2026-10-19T12:05:00Z");

        var response = BranchPairingChallengeResponseMapper.Map(
            "https://192.168.2.2:8443",
            " BRANCH-01 ",
            new IssuedBranchPairingChallenge(
                issued.PairingId,
                issued.Token,
                expiresAt));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal("https://192.168.2.2:8443/", response.BranchServiceUrl);
        Assert.Equal("BRANCH-01", response.BranchId);
        Assert.Equal(issued.PairingId, response.PairingId);
        Assert.Equal(issued.Token, response.OneTimeToken);
        Assert.Equal(expiresAt, response.ExpiresAt);
    }

    [Theory]
    [InlineData("http://192.168.2.2")]
    [InlineData("https://branch-01.pos.internal")]
    [InlineData("https://user:password@192.168.2.2")]
    [InlineData("https://192.168.2.2/pair")]
    [InlineData("https://192.168.2.2/?branch=01")]
    [InlineData("https://192.168.2.2/#pair")]
    [InlineData("192.168.2.2")]
    public void Non_phase_zero_service_url_is_rejected(string serviceUrl)
    {
        var challenge = Challenge();

        Assert.Throws<ArgumentException>(() =>
            BranchPairingChallengeResponseMapper.Map(
                serviceUrl,
                "BRANCH-01",
                challenge));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_branch_identity_is_rejected(string branchId)
    {
        Assert.Throws<ArgumentException>(() =>
            BranchPairingChallengeResponseMapper.Map(
                "https://192.168.2.2",
                branchId,
                Challenge()));
    }

    [Fact]
    public void Noncanonical_pairing_identity_is_rejected()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var challenge = new IssuedBranchPairingChallenge(
            "not-canonical",
            issued.Token,
            DateTimeOffset.Parse("2026-10-19T12:05:00Z"));

        Assert.Throws<ArgumentException>(() =>
            BranchPairingChallengeResponseMapper.Map(
                "https://192.168.2.2",
                "BRANCH-01",
                challenge));
    }

    [Fact]
    public void Noncanonical_one_time_token_is_rejected()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var challenge = new IssuedBranchPairingChallenge(
            issued.PairingId,
            "v1.invalid",
            DateTimeOffset.Parse("2026-10-19T12:05:00Z"));

        Assert.Throws<ArgumentException>(() =>
            BranchPairingChallengeResponseMapper.Map(
                "https://192.168.2.2",
                "BRANCH-01",
                challenge));
    }

    [Fact]
    public void Non_utc_expiry_is_rejected()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var challenge = new IssuedBranchPairingChallenge(
            issued.PairingId,
            issued.Token,
            new DateTimeOffset(2026, 10, 19, 19, 5, 0, TimeSpan.FromHours(7)));

        Assert.Throws<ArgumentException>(() =>
            BranchPairingChallengeResponseMapper.Map(
                "https://192.168.2.2",
                "BRANCH-01",
                challenge));
    }

    [Fact]
    public void Public_json_contains_no_creator_or_permanent_credential()
    {
        var response = BranchPairingChallengeResponseMapper.Map(
            "https://[2001:db8::10]:8443",
            "BRANCH-01",
            Challenge());
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("https://[2001:db8::10]:8443/", root.GetProperty("branchServiceUrl").GetString());
        Assert.True(root.TryGetProperty("oneTimeToken", out _));
        Assert.False(root.TryGetProperty("createdByEmployeeId", out _));
        Assert.False(root.TryGetProperty("deviceCredential", out _));
        Assert.False(root.TryGetProperty("pin", out _));
    }

    private static IssuedBranchPairingChallenge Challenge()
    {
        var issued = BranchPairingTokenFormat.Generate();
        return new IssuedBranchPairingChallenge(
            issued.PairingId,
            issued.Token,
            DateTimeOffset.Parse("2026-10-19T12:05:00Z"));
    }
}
