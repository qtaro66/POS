using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Pos.Application.Security;

public sealed class IssuedBranchEmployeeSessionToken
{
    private readonly byte[] _secretHash;

    internal IssuedBranchEmployeeSessionToken(
        string sessionId,
        string token,
        byte[] secretHash)
    {
        SessionId = sessionId;
        Token = token;
        _secretHash = secretHash;
    }

    public string SessionId { get; }

    public string Token { get; }

    public byte[] CopySecretHash() => _secretHash.ToArray();
}

public sealed class ParsedBranchEmployeeSessionToken : IDisposable
{
    private byte[]? _secret;

    internal ParsedBranchEmployeeSessionToken(string sessionId, byte[] secret)
    {
        SessionId = sessionId;
        _secret = secret;
    }

    public string SessionId { get; }

    public ReadOnlyMemory<byte> Secret => _secret is null
        ? throw new ObjectDisposedException(nameof(ParsedBranchEmployeeSessionToken))
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

public static class BranchEmployeeSessionTokenFormat
{
    public const string Version = "es1";
    public const int SessionIdByteLength = 16;
    public const int SecretByteLength = 32;
    public const int SecretHashByteLength = 32;

    private const int SessionIdEncodedLength = 22;
    private const int SecretEncodedLength = 43;
    private const int TokenLength = 70;

    public static IssuedBranchEmployeeSessionToken Generate()
    {
        var sessionIdBytes = new byte[SessionIdByteLength];
        var secret = new byte[SecretByteLength];
        try
        {
            RandomNumberGenerator.Fill(sessionIdBytes);
            RandomNumberGenerator.Fill(secret);
            var sessionId = EncodeBase64Url(sessionIdBytes);
            var encodedSecret = EncodeBase64Url(secret);
            var token = $"{Version}.{sessionId}.{encodedSecret}";
            var secretHash = SHA256.HashData(secret);
            return new IssuedBranchEmployeeSessionToken(
                sessionId,
                token,
                secretHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionIdBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static bool IsCanonicalSessionId(string? sessionId)
    {
        if (sessionId is null ||
            sessionId.Length != SessionIdEncodedLength ||
            !IsBase64Url(sessionId))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[SessionIdByteLength];
        try
        {
            return TryDecodeBase64Url(sessionId, decoded) &&
                sessionId.AsSpan().SequenceEqual(EncodeBase64Url(decoded));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    public static bool TryParse(
        string? token,
        [NotNullWhen(true)] out ParsedBranchEmployeeSessionToken? session)
    {
        if (token is null)
        {
            session = null;
            return false;
        }

        return TryParse(token.AsSpan(), out session);
    }

    public static bool TryParse(
        ReadOnlySpan<char> token,
        [NotNullWhen(true)] out ParsedBranchEmployeeSessionToken? session)
    {
        session = null;
        var firstSeparator = Version.Length;
        var secondSeparator = Version.Length + 1 + SessionIdEncodedLength;
        if (token.Length != TokenLength ||
            !token[..Version.Length].SequenceEqual(Version) ||
            token[firstSeparator] != '.' ||
            token[secondSeparator] != '.')
        {
            return false;
        }

        var sessionIdSpan = token.Slice(
            firstSeparator + 1,
            SessionIdEncodedLength);
        var secretSpan = token.Slice(secondSeparator + 1, SecretEncodedLength);
        if (!IsBase64Url(sessionIdSpan) || !IsBase64Url(secretSpan))
        {
            return false;
        }

        var sessionIdBytes = new byte[SessionIdByteLength];
        var secret = new byte[SecretByteLength];
        try
        {
            if (!TryDecodeBase64Url(sessionIdSpan, sessionIdBytes) ||
                !TryDecodeBase64Url(secretSpan, secret))
            {
                return false;
            }

            session = new ParsedBranchEmployeeSessionToken(
                sessionIdSpan.ToString(),
                secret);
            secret = [];
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionIdBytes);
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
