using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Pos.Application.Security;

public sealed class IssuedBranchPairingToken
{
    private readonly byte[] _secretHash;

    internal IssuedBranchPairingToken(
        string pairingId,
        string token,
        byte[] secretHash)
    {
        PairingId = pairingId;
        Token = token;
        _secretHash = secretHash;
    }

    public string PairingId { get; }

    public string Token { get; }

    public byte[] CopySecretHash() => _secretHash.ToArray();
}

public sealed class ParsedBranchPairingToken : IDisposable
{
    private byte[]? _secret;

    internal ParsedBranchPairingToken(byte[] secret)
    {
        _secret = secret;
    }

    public ReadOnlyMemory<byte> Secret => _secret is null
        ? throw new ObjectDisposedException(nameof(ParsedBranchPairingToken))
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

public static class BranchPairingTokenFormat
{
    public const string Version = "v1";
    public const int PairingIdByteLength = 16;
    public const int SecretByteLength = 32;
    public const int SecretHashByteLength = 32;

    private const int PairingIdEncodedLength = 22;
    private const int SecretEncodedLength = 43;
    private const int TokenLength = 46;

    public static IssuedBranchPairingToken Generate()
    {
        var pairingIdBytes = new byte[PairingIdByteLength];
        var secret = new byte[SecretByteLength];
        try
        {
            RandomNumberGenerator.Fill(pairingIdBytes);
            RandomNumberGenerator.Fill(secret);
            var pairingId = EncodeBase64Url(pairingIdBytes);
            var token = $"{Version}.{EncodeBase64Url(secret)}";
            var secretHash = SHA256.HashData(secret);
            return new IssuedBranchPairingToken(pairingId, token, secretHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairingIdBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static bool IsCanonicalPairingId(string? pairingId)
    {
        if (pairingId is null ||
            pairingId.Length != PairingIdEncodedLength ||
            !IsBase64Url(pairingId))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[PairingIdByteLength];
        try
        {
            return TryDecodeBase64Url(pairingId, decoded) &&
                pairingId.AsSpan().SequenceEqual(EncodeBase64Url(decoded));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    public static bool TryParse(
        string? token,
        [NotNullWhen(true)] out ParsedBranchPairingToken? pairingToken)
    {
        pairingToken = null;
        if (token is null ||
            token.Length != TokenLength ||
            !token.AsSpan(0, Version.Length).SequenceEqual(Version) ||
            token[Version.Length] != '.')
        {
            return false;
        }

        var secretSpan = token.AsSpan(Version.Length + 1, SecretEncodedLength);
        if (!IsBase64Url(secretSpan))
        {
            return false;
        }

        var secret = new byte[SecretByteLength];
        try
        {
            if (!TryDecodeBase64Url(secretSpan, secret) ||
                !secretSpan.SequenceEqual(EncodeBase64Url(secret)))
            {
                return false;
            }

            pairingToken = new ParsedBranchPairingToken(secret);
            secret = [];
            return true;
        }
        finally
        {
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
            bytesWritten == destination.Length;
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
