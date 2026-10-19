using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchPairingTokenFormatTests
{
    [Fact]
    public void Generated_token_has_distinct_canonical_id_and_matching_hash()
    {
        var issued = BranchPairingTokenFormat.Generate();

        Assert.Equal(22, issued.PairingId.Length);
        Assert.True(BranchPairingTokenFormat.IsCanonicalPairingId(issued.PairingId));
        Assert.Equal(46, issued.Token.Length);
        Assert.StartsWith("v1.", issued.Token, StringComparison.Ordinal);
        Assert.Equal(
            BranchPairingTokenFormat.SecretHashByteLength,
            issued.CopySecretHash().Length);
        Assert.True(BranchPairingTokenFormat.TryParse(issued.Token, out var parsed));
        using (var token = Assert.IsType<ParsedBranchPairingToken>(parsed))
        {
            Assert.Equal(BranchPairingTokenFormat.SecretByteLength, token.Secret.Length);
            Assert.True(BranchPairingTokenFormat.VerifySecretHash(
                issued.CopySecretHash(),
                token.Secret.Span));
        }
    }

    [Fact]
    public void Generated_pairing_tokens_are_distinct()
    {
        var first = BranchPairingTokenFormat.Generate();
        var second = BranchPairingTokenFormat.Generate();

        Assert.NotEqual(first.PairingId, second.PairingId);
        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.CopySecretHash(), second.CopySecretHash());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    public void Noncanonical_pairing_id_is_rejected(string? pairingId)
    {
        Assert.False(BranchPairingTokenFormat.IsCanonicalPairingId(pairingId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.short")]
    [InlineData("v2.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("V1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.")]
    public void Invalid_token_is_rejected_without_partial_result(string? token)
    {
        Assert.False(BranchPairingTokenFormat.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Noncanonical_base64url_alias_is_rejected()
    {
        var token = $"v1.{new string('A', 42)}B";

        Assert.False(BranchPairingTokenFormat.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Wrong_secret_does_not_match_hash()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var wrongSecret = new byte[BranchPairingTokenFormat.SecretByteLength];

        Assert.False(BranchPairingTokenFormat.VerifySecretHash(
            issued.CopySecretHash(),
            wrongSecret));
    }

    [Fact]
    public void Parsed_secret_is_cleared_and_unavailable_after_dispose()
    {
        var issued = BranchPairingTokenFormat.Generate();
        Assert.True(BranchPairingTokenFormat.TryParse(issued.Token, out var parsed));
        var token = Assert.IsType<ParsedBranchPairingToken>(parsed);
        var captured = token.Secret;

        token.Dispose();

        Assert.All(captured.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => token.Secret.ToArray());
    }

    [Fact]
    public void Returned_hash_is_a_copy()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var firstCopy = issued.CopySecretHash();
        firstCopy[0] ^= byte.MaxValue;

        var secondCopy = issued.CopySecretHash();

        Assert.NotEqual(firstCopy, secondCopy);
        Assert.True(BranchPairingTokenFormat.TryParse(issued.Token, out var parsed));
        using (var token = Assert.IsType<ParsedBranchPairingToken>(parsed))
        {
            Assert.True(BranchPairingTokenFormat.VerifySecretHash(
                secondCopy,
                token.Secret.Span));
        }
    }

    [Theory]
    [InlineData(31, 32)]
    [InlineData(32, 31)]
    [InlineData(33, 32)]
    [InlineData(32, 33)]
    public void Unexpected_hash_or_secret_length_is_rejected(
        int hashLength,
        int secretLength)
    {
        Assert.False(BranchPairingTokenFormat.VerifySecretHash(
            new byte[hashLength],
            new byte[secretLength]));
    }
}
