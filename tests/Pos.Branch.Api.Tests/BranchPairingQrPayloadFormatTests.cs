using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingQrPayloadFormatTests
{
    [Fact]
    public void Challenge_formats_as_canonical_fragment_payload()
    {
        var response = Response("BRANCH 01/หน้าร้าน");

        var payload = BranchPairingQrPayloadFormat.Create(response);
        var uri = new Uri(payload);

        Assert.StartsWith(
            "https://192.168.2.2:8443/#pair?v=1&branchId=",
            payload,
            StringComparison.Ordinal);
        Assert.Contains("BRANCH%2001%2F", payload, StringComparison.Ordinal);
        Assert.Equal(string.Empty, uri.Query);
        Assert.DoesNotContain(response.OneTimeToken, uri.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains(response.OneTimeToken, uri.Fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_payload_round_trips_all_required_pairing_fields()
    {
        var response = Response("BRANCH-01");
        var payload = BranchPairingQrPayloadFormat.Create(response);

        Assert.True(BranchPairingQrPayloadFormat.TryParse(payload, out var parsed));
        var result = Assert.IsType<ParsedBranchPairingQrPayload>(parsed);
        Assert.Equal(response.BranchServiceUrl, result.BranchServiceUrl);
        Assert.Equal(response.BranchId, result.BranchId);
        Assert.Equal(response.PairingId, result.PairingId);
        Assert.Equal(response.OneTimeToken, result.OneTimeToken);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void Unsupported_response_schema_is_rejected(int schemaVersion)
    {
        var response = Response("BRANCH-01") with { SchemaVersion = schemaVersion };

        Assert.Throws<ArgumentException>(() =>
            BranchPairingQrPayloadFormat.Create(response));
    }

    [Theory]
    [InlineData(" BRANCH-01")]
    [InlineData("BRANCH-01 ")]
    [InlineData("")]
    public void Noncanonical_response_branch_identity_is_rejected(string branchId)
    {
        Assert.Throws<ArgumentException>(() =>
            BranchPairingQrPayloadFormat.Create(Response(branchId)));
    }

    [Fact]
    public void Noncanonical_response_pairing_identity_is_rejected()
    {
        var response = Response("BRANCH-01") with { PairingId = "not-canonical" };

        Assert.Throws<ArgumentException>(() =>
            BranchPairingQrPayloadFormat.Create(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" https://192.168.2.2/#pair?v=1&branchId=x&pairingId=y&token=z")]
    [InlineData("http://192.168.2.2/#pair?v=1&branchId=x&pairingId=y&token=z")]
    [InlineData("https://branch.pos.internal/#pair?v=1&branchId=x&pairingId=y&token=z")]
    [InlineData("https://192.168.2.2/pair#pair?v=1&branchId=x&pairingId=y&token=z")]
    [InlineData("https://192.168.2.2/?token=secret#pair?v=1&branchId=x&pairingId=y&token=z")]
    [InlineData("https://192.168.2.2/#pair?v=2&branchId=x&pairingId=y&token=z")]
    public void Invalid_outer_payload_is_rejected(string? payload)
    {
        Assert.False(BranchPairingQrPayloadFormat.TryParse(payload, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Reordered_or_duplicated_fields_are_rejected()
    {
        var response = Response("BRANCH-01");
        var canonical = BranchPairingQrPayloadFormat.Create(response);
        var reordered = canonical.Replace(
            $"branchId={response.BranchId}&pairingId={response.PairingId}",
            $"pairingId={response.PairingId}&branchId={response.BranchId}",
            StringComparison.Ordinal);
        var duplicated = string.Concat(canonical, "&branchId=BRANCH-01");

        Assert.False(BranchPairingQrPayloadFormat.TryParse(reordered, out _));
        Assert.False(BranchPairingQrPayloadFormat.TryParse(duplicated, out _));
    }

    [Fact]
    public void Noncanonical_percent_encoding_is_rejected()
    {
        var response = Response("BRANCH/01");
        var canonical = BranchPairingQrPayloadFormat.Create(response);
        var lowercaseEscape = canonical.Replace("%2F", "%2f", StringComparison.Ordinal);

        Assert.False(BranchPairingQrPayloadFormat.TryParse(lowercaseEscape, out _));
    }

    [Fact]
    public void Invalid_pairing_token_is_rejected_without_partial_result()
    {
        var response = Response("BRANCH-01");
        var canonical = BranchPairingQrPayloadFormat.Create(response);
        var invalid = canonical.Replace(
            response.OneTimeToken,
            "v1.invalid",
            StringComparison.Ordinal);

        Assert.False(BranchPairingQrPayloadFormat.TryParse(invalid, out var parsed));
        Assert.Null(parsed);
    }

    private static BranchPairingChallengeResponse Response(string branchId)
    {
        var issued = BranchPairingTokenFormat.Generate();
        return new BranchPairingChallengeResponse(
            BranchPairingChallengeResponseMapper.CurrentSchemaVersion,
            "https://192.168.2.2:8443/",
            branchId,
            issued.PairingId,
            issued.Token,
            DateTimeOffset.Parse("2026-10-19T12:05:00Z"));
    }
}
