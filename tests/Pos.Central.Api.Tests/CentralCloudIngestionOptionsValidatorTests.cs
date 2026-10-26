using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionOptionsValidatorTests
{
    [Fact]
    public void Defaults_keep_ingestion_disabled_without_a_scheme()
    {
        var options = new CentralCloudIngestionOptions();

        var result = Validate(options);

        Assert.False(options.Enabled);
        Assert.Equal(string.Empty, options.AuthenticationScheme);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("CentralBranchCertificate")]
    [InlineData("central-branch-v1")]
    [InlineData("central.branch_v1")]
    public void Enabled_ingestion_accepts_canonical_scheme(string scheme)
    {
        var result = Validate(new CentralCloudIngestionOptions
        {
            Enabled = true,
            AuthenticationScheme = scheme
        });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" central-branch")]
    [InlineData("central-branch ")]
    [InlineData("central branch")]
    [InlineData("central/branch")]
    [InlineData(".central-branch")]
    [InlineData("_central-branch")]
    [InlineData("-central-branch")]
    [InlineData("สาขา")]
    public void Enabled_ingestion_rejects_missing_or_noncanonical_scheme(
        string scheme)
    {
        var result = Validate(new CentralCloudIngestionOptions
        {
            Enabled = true,
            AuthenticationScheme = scheme
        });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "canonical ASCII identifier",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_ingestion_rejects_overlong_scheme()
    {
        var result = Validate(new CentralCloudIngestionOptions
        {
            Enabled = true,
            AuthenticationScheme = new string('a', 129)
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Disabled_ingestion_ignores_unconfigured_scheme()
    {
        var result = Validate(new CentralCloudIngestionOptions
        {
            Enabled = false,
            AuthenticationScheme = " "
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_options_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CentralCloudIngestionOptionsValidator().Validate(null, null!));
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        CentralCloudIngestionOptions options) =>
        new CentralCloudIngestionOptionsValidator().Validate(null, options);
}
