using System.Security.Cryptography;

namespace Pos.Application.Security;

public static class BranchDeviceIdentifierFormat
{
    public const int ByteLength = 16;
    public const int EncodedLength = 22;

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[ByteLength];
        try
        {
            RandomNumberGenerator.Fill(bytes);
            return EncodeBase64Url(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static bool IsCanonical(string? deviceId)
    {
        if (deviceId is null ||
            deviceId.Length != EncodedLength ||
            !IsBase64Url(deviceId))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[ByteLength];
        try
        {
            return TryDecodeBase64Url(deviceId, decoded) &&
                deviceId.AsSpan().SequenceEqual(EncodeBase64Url(decoded));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
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
