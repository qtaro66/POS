using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchDeviceCredentialFormatTests
{
    [Fact]
    public void Generated_credential_has_bounded_v1_shape_and_matching_hash()
    {
        var issued = BranchDeviceCredentialFormat.Generate();

        Assert.Equal(22, issued.CredentialId.Length);
        Assert.True(BranchDeviceCredentialFormat.IsCanonicalCredentialId(
            issued.CredentialId));
        Assert.Equal(69, issued.Token.Length);
        Assert.StartsWith($"v1.{issued.CredentialId}.", issued.Token, StringComparison.Ordinal);
        Assert.Equal(
            BranchDeviceCredentialFormat.SecretHashByteLength,
            issued.CopySecretHash().Length);
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        using (var credential = Assert.IsType<ParsedBranchDeviceCredential>(parsed))
        {
            Assert.Equal(issued.CredentialId, credential.CredentialId);
            Assert.Equal(
                BranchDeviceCredentialFormat.SecretByteLength,
                credential.Secret.Length);
            Assert.True(BranchDeviceCredentialFormat.VerifySecretHash(
                issued.CopySecretHash(),
                credential.Secret.Span));
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
    public void Noncanonical_credential_identifier_is_rejected(string? credentialId)
    {
        Assert.False(BranchDeviceCredentialFormat.IsCanonicalCredentialId(credentialId));
    }

    [Fact]
    public void Generated_credentials_are_distinct()
    {
        var first = BranchDeviceCredentialFormat.Generate();
        var second = BranchDeviceCredentialFormat.Generate();

        Assert.NotEqual(first.CredentialId, second.CredentialId);
        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.CopySecretHash(), second.CopySecretHash());
    }

    [Fact]
    public void Wrong_secret_does_not_match_hash()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        var wrongSecret = new byte[BranchDeviceCredentialFormat.SecretByteLength];

        Assert.False(BranchDeviceCredentialFormat.VerifySecretHash(
            issued.CopySecretHash(),
            wrongSecret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.short.secret")]
    [InlineData("v2.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAA!.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.")]
    public void Invalid_token_is_rejected_without_partial_result(string? token)
    {
        Assert.False(BranchDeviceCredentialFormat.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Noncanonical_base64url_alias_is_rejected()
    {
        var token = $"v1.{new string('A', 22)}.{new string('A', 42)}B";

        Assert.False(BranchDeviceCredentialFormat.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Parsed_secret_is_cleared_and_unavailable_after_dispose()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        var credential = Assert.IsType<ParsedBranchDeviceCredential>(parsed);
        var captured = credential.Secret;

        credential.Dispose();

        Assert.All(captured.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => credential.Secret.ToArray());
    }

    [Fact]
    public void Returned_hash_is_a_copy()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        var firstCopy = issued.CopySecretHash();
        firstCopy[0] ^= byte.MaxValue;

        var secondCopy = issued.CopySecretHash();

        Assert.NotEqual(firstCopy, secondCopy);
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        using (var credential = Assert.IsType<ParsedBranchDeviceCredential>(parsed))
        {
            Assert.True(BranchDeviceCredentialFormat.VerifySecretHash(
                secondCopy,
                credential.Secret.Span));
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
        Assert.False(BranchDeviceCredentialFormat.VerifySecretHash(
            new byte[hashLength],
            new byte[secretLength]));
    }
}
