using System.Security.Cryptography;

namespace Pos.Application.Security;

public sealed class IssuedBranchDeviceCredential
{
    private readonly byte[] _secretHash;

    internal IssuedBranchDeviceCredential(
        string credentialId,
        string token,
        byte[] secretHash)
    {
        CredentialId = credentialId;
        Token = token;
        _secretHash = secretHash;
    }

    public string CredentialId { get; }

    public string Token { get; }

    public byte[] CopySecretHash() => _secretHash.ToArray();
}

public sealed class ParsedBranchDeviceCredential : IDisposable
{
    private byte[]? _secret;

    internal ParsedBranchDeviceCredential(string credentialId, byte[] secret)
    {
        CredentialId = credentialId;
        _secret = secret;
    }

    public string CredentialId { get; }

    public ReadOnlyMemory<byte> Secret => _secret is null
        ? throw new ObjectDisposedException(nameof(ParsedBranchDeviceCredential))
        : _secret;

    public void Dispose()
    {
        var secret = Interlocked.Exchange(ref _secret, null);
        if (secret is not null)
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}

public static class BranchDeviceCredentialFormat
{
    public const string Version = "v1";
    public const int CredentialIdByteLength = 16;
    public const int SecretByteLength = 32;
    public const int SecretHashByteLength = 32;

    private const int CredentialIdEncodedLength = 22;
    private const int SecretEncodedLength = 43;
    private const int TokenLength = 69;

    public static IssuedBranchDeviceCredential Generate()
    {
        var credentialIdBytes = new byte[CredentialIdByteLength];
        var secret = new byte[SecretByteLength];
        try
        {
            RandomNumberGenerator.Fill(credentialIdBytes);
            RandomNumberGenerator.Fill(secret);
            var credentialId = EncodeBase64Url(credentialIdBytes);
            var encodedSecret = EncodeBase64Url(secret);
            var token = $"{Version}.{credentialId}.{encodedSecret}";
            var secretHash = SHA256.HashData(secret);
            return new IssuedBranchDeviceCredential(credentialId, token, secretHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialIdBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static bool TryParse(
        string? token,
        out ParsedBranchDeviceCredential? credential)
    {
        credential = null;
        if (token is null || token.Length != TokenLength)
        {
            return false;
        }

        var firstSeparator = token.IndexOf('.', StringComparison.Ordinal);
        var secondSeparator = token.IndexOf('.', firstSeparator + 1);
        if (firstSeparator != Version.Length ||
            secondSeparator != Version.Length + 1 + CredentialIdEncodedLength ||
            token.IndexOf('.', secondSeparator + 1) >= 0 ||
            !token.AsSpan(0, firstSeparator).SequenceEqual(Version))
        {
            return false;
        }

        var credentialIdSpan = token.AsSpan(
            firstSeparator + 1,
            CredentialIdEncodedLength);
        var secretSpan = token.AsSpan(secondSeparator + 1, SecretEncodedLength);
        if (!IsBase64Url(credentialIdSpan) || !IsBase64Url(secretSpan))
        {
            return false;
        }

        var credentialIdBytes = new byte[CredentialIdByteLength];
        var secret = new byte[SecretByteLength];
        try
        {
            if (!TryDecodeBase64Url(credentialIdSpan, credentialIdBytes) ||
                !TryDecodeBase64Url(secretSpan, secret))
            {
                return false;
            }

            credential = new ParsedBranchDeviceCredential(
                credentialIdSpan.ToString(),
                secret);
            secret = [];
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialIdBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static bool VerifySecretHash(
        ReadOnlySpan<byte> storedSecretHash,
        ReadOnlySpan<byte> presentedSecret)
    {
        if (storedSecretHash.Length != SecretHashByteLength ||
            presentedSecret.Length != SecretByteLength)
        {
            return false;
        }

        Span<byte> presentedHash = stackalloc byte[SecretHashByteLength];
        try
        {
            SHA256.HashData(presentedSecret, presentedHash);
            return CryptographicOperations.FixedTimeEquals(
                storedSecretHash,
                presentedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presentedHash);
        }
    }

    private static string EncodeBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecodeBase64Url(
        ReadOnlySpan<char> value,
        Span<byte> destination)
    {
        var paddingLength = (4 - (value.Length % 4)) % 4;
        var padded = string.Concat(value, new string('=', paddingLength))
            .Replace('-', '+')
            .Replace('_', '/');
        return Convert.TryFromBase64String(padded, destination, out var bytesWritten) &&
            bytesWritten == destination.Length &&
            value.SequenceEqual(EncodeBase64Url(destination));
    }

    private static bool IsBase64Url(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if ((character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character is '-' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
