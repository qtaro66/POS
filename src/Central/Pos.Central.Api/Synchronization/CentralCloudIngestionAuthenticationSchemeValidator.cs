using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralCloudIngestionAuthenticationSchemeValidator
    : IValidateOptions<CentralCloudIngestionOptions>
{
    private readonly IServiceProvider _services;

    public CentralCloudIngestionAuthenticationSchemeValidator(
        IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ValidateOptionsResult Validate(
        string? name,
        CentralCloudIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled ||
            !CentralCloudIngestionOptionsValidator
                .IsCanonicalAuthenticationScheme(options.AuthenticationScheme))
        {
            return ValidateOptionsResult.Success;
        }

        var schemeProvider = _services.GetService<IAuthenticationSchemeProvider>();
        var scheme = schemeProvider?
            .GetSchemeAsync(options.AuthenticationScheme)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        return scheme is not null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"AuthenticationScheme '{options.AuthenticationScheme}' must " +
                "reference a registered authentication handler when Central " +
                "Cloud ingestion is enabled.");
    }
}
