using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchDeviceIdentifierFormatTests
{
    [Fact]
    public void Generated_identifier_is_canonical_and_bounded()
    {
        var deviceId = BranchDeviceIdentifierFormat.Generate();

        Assert.Equal(BranchDeviceIdentifierFormat.EncodedLength, deviceId.Length);
        Assert.True(BranchDeviceIdentifierFormat.IsCanonical(deviceId));
    }

    [Fact]
    public void Generated_identifiers_are_distinct()
    {
        Assert.NotEqual(
            BranchDeviceIdentifierFormat.Generate(),
            BranchDeviceIdentifierFormat.Generate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA!")]
    public void Noncanonical_identifier_is_rejected(string? deviceId)
    {
        Assert.False(BranchDeviceIdentifierFormat.IsCanonical(deviceId));
    }

    [Fact]
    public void Base64url_alias_is_rejected()
    {
        Assert.False(BranchDeviceIdentifierFormat.IsCanonical(
            "AAAAAAAAAAAAAAAAAAAAAB"));
    }
}
