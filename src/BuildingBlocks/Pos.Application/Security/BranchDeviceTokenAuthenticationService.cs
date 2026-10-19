namespace Pos.Application.Security;

public sealed class BranchDeviceTokenAuthenticationService
{
    private readonly BranchDeviceCredentialAuthenticationService _credentialAuthentication;

    public BranchDeviceTokenAuthenticationService(
        IBranchDeviceCredentialValidator validator)
    {
        _credentialAuthentication = new BranchDeviceCredentialAuthenticationService(
            validator ?? throw new ArgumentNullException(nameof(validator)));
    }

    public async Task<VerifiedBranchDevice?> AuthenticateAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (!BranchDeviceCredentialFormat.TryParse(token, out var parsedCredential))
        {
            return null;
        }

        using (parsedCredential)
        {
            return await _credentialAuthentication.AuthenticateAsync(
                parsedCredential.CredentialId,
                parsedCredential.Secret,
                cancellationToken);
        }
    }
}
