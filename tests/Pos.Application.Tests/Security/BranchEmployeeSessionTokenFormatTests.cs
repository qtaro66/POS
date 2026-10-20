using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchEmployeeSessionTokenFormatTests
{
    [Fact]
    public void Generated_token_has_bounded_es1_shape_and_matching_hash()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();

        Assert.Equal(22, issued.SessionId.Length);
        Assert.True(BranchEmployeeSessionTokenFormat.IsCanonicalSessionId(
            issued.SessionId));
        Assert.Equal(70, issued.Token.Length);
        Assert.StartsWith(
            $"es1.{issued.SessionId}.",
            issued.Token,
            StringComparison.Ordinal);
        Assert.Equal(
            BranchEmployeeSessionTokenFormat.SecretHashByteLength,
            issued.CopySecretHash().Length);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(
            issued.Token,
            out var parsed));
        using (var session = Assert.IsType<ParsedBranchEmployeeSessionToken>(parsed))
        {
            Assert.Equal(issued.SessionId, session.SessionId);
            Assert.Equal(
                BranchEmployeeSessionTokenFormat.SecretByteLength,
                session.Secret.Length);
            Assert.True(BranchEmployeeSessionTokenFormat.VerifySecretHash(
                issued.CopySecretHash(),
                session.Secret.Span));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAB")]
    public void Noncanonical_session_identifier_is_rejected(string? sessionId)
    {
        Assert.False(BranchEmployeeSessionTokenFormat.IsCanonicalSessionId(sessionId));
    }

    [Fact]
    public void Generated_tokens_are_distinct()
    {
        var first = BranchEmployeeSessionTokenFormat.Generate();
        var second = BranchEmployeeSessionTokenFormat.Generate();

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.CopySecretHash(), second.CopySecretHash());
    }

    [Fact]
    public void Wrong_secret_does_not_match_hash()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var wrongSecret = new byte[BranchEmployeeSessionTokenFormat.SecretByteLength];

        Assert.False(BranchEmployeeSessionTokenFormat.VerifySecretHash(
            issued.CopySecretHash(),
            wrongSecret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("es1.short.secret")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("es2.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("es1.AAAAAAAAAAAAAAAAAAAAA!.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("es1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("es1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.")]
    public void Invalid_token_is_rejected_without_partial_result(string? token)
    {
        Assert.False(BranchEmployeeSessionTokenFormat.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Noncanonical_base64url_alias_is_rejected()
    {
        var token = $"es1.{new string('A', 22)}.{new string('A', 42)}B";

        Assert.False(BranchEmployeeSessionTokenFormat.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Parsed_secret_is_cleared_and_unavailable_after_dispose()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(
            issued.Token,
            out var parsed));
        var session = Assert.IsType<ParsedBranchEmployeeSessionToken>(parsed);
        var captured = session.Secret;

        session.Dispose();

        Assert.All(captured.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => session.Secret.ToArray());
    }

    [Fact]
    public void Returned_hash_is_a_copy()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var firstCopy = issued.CopySecretHash();
        firstCopy[0] ^= byte.MaxValue;

        var secondCopy = issued.CopySecretHash();

        Assert.NotEqual(firstCopy, secondCopy);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(
            issued.Token,
            out var parsed));
        using (var session = Assert.IsType<ParsedBranchEmployeeSessionToken>(parsed))
        {
            Assert.True(BranchEmployeeSessionTokenFormat.VerifySecretHash(
                secondCopy,
                session.Secret.Span));
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
        Assert.False(BranchEmployeeSessionTokenFormat.VerifySecretHash(
            new byte[hashLength],
            new byte[secretLength]));
    }
}
