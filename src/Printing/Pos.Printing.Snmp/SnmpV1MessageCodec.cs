using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Printing.Snmp;

internal enum SnmpV1RequestType : byte
{
    Get = 0xA0,
    GetNext = 0xA1
}

internal static class SnmpV1MessageCodec
{
    private const int MaximumPacketBytes = 65_535;

    public static byte[] EncodeRequest(
        SnmpV1RequestType requestType,
        int requestId,
        ReadOnlySpan<byte> community,
        string oid)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        ValidateCommunity(community);
        var variableBinding = Wrap(
            0x30,
            Wrap(0x06, EncodeOid(oid)),
            Wrap(0x05, []));
        var requestPdu = Wrap(
            (byte)requestType,
            Wrap(0x02, EncodeInteger(requestId)),
            Wrap(0x02, [0x00]),
            Wrap(0x02, [0x00]),
            Wrap(0x30, variableBinding));
        var packet = Wrap(
            0x30,
            Wrap(0x02, [0x00]),
            Wrap(0x04, community.ToArray()),
            requestPdu);
        if (packet.Length > MaximumPacketBytes)
        {
            throw new InvalidOperationException("The SNMP request exceeds the packet limit.");
        }

        return packet;
    }

    public static SnmpV1Variable DecodeResponse(
        ReadOnlyMemory<byte> response,
        int expectedRequestId,
        ReadOnlySpan<byte> expectedCommunity)
    {
        if (response.IsEmpty || response.Length > MaximumPacketBytes)
        {
            throw new InvalidDataException("The SNMP response size is invalid.");
        }

        ValidateCommunity(expectedCommunity);
        var bytes = response.ToArray();
        var offset = 0;
        var message = ReadTlv(bytes, ref offset, bytes.Length);
        RequireTag(message, 0x30, "message");
        if (offset != bytes.Length)
        {
            throw new InvalidDataException("The SNMP response has trailing data.");
        }

        var messageOffset = message.ContentOffset;
        var version = ReadTlv(bytes, ref messageOffset, message.EndOffset);
        if (ReadInteger(bytes, version) != 0)
        {
            throw new InvalidDataException("The SNMP response is not version 1.");
        }

        var community = ReadTlv(bytes, ref messageOffset, message.EndOffset);
        RequireTag(community, 0x04, "community");
        var communityBytes = bytes.AsSpan(community.ContentOffset, community.Length);
        if (communityBytes.Length != expectedCommunity.Length ||
            !CryptographicOperations.FixedTimeEquals(communityBytes, expectedCommunity))
        {
            throw new InvalidDataException("The SNMP response community does not match.");
        }

        var responsePdu = ReadTlv(bytes, ref messageOffset, message.EndOffset);
        RequireTag(responsePdu, 0xA2, "GetResponse PDU");
        RequireAtEnd(messageOffset, message.EndOffset, "SNMP message");

        var pduOffset = responsePdu.ContentOffset;
        var requestId = ReadInteger(bytes, ReadTlv(bytes, ref pduOffset, responsePdu.EndOffset));
        if (requestId != expectedRequestId)
        {
            throw new InvalidDataException("The SNMP response request ID does not match.");
        }

        var errorStatus = ReadInteger(bytes, ReadTlv(bytes, ref pduOffset, responsePdu.EndOffset));
        var errorIndex = ReadInteger(bytes, ReadTlv(bytes, ref pduOffset, responsePdu.EndOffset));
        if (errorStatus != 0)
        {
            throw new SnmpV1AgentException(errorStatus, errorIndex);
        }

        var variableBindingList = ReadTlv(bytes, ref pduOffset, responsePdu.EndOffset);
        RequireTag(variableBindingList, 0x30, "variable-binding list");
        RequireAtEnd(pduOffset, responsePdu.EndOffset, "GetResponse PDU");

        var listOffset = variableBindingList.ContentOffset;
        var variableBinding = ReadTlv(bytes, ref listOffset, variableBindingList.EndOffset);
        RequireTag(variableBinding, 0x30, "variable binding");
        RequireAtEnd(listOffset, variableBindingList.EndOffset, "variable-binding list");

        var bindingOffset = variableBinding.ContentOffset;
        var oid = DecodeOid(bytes, ReadTlv(bytes, ref bindingOffset, variableBinding.EndOffset));
        var valueTlv = ReadTlv(bytes, ref bindingOffset, variableBinding.EndOffset);
        RequireAtEnd(bindingOffset, variableBinding.EndOffset, "variable binding");

        SnmpV1Value value = valueTlv.Tag switch
        {
            0x02 => new SnmpV1IntegerValue(ReadInteger(bytes, valueTlv)),
            0x04 => new SnmpV1OctetStringValue(
                Encoding.ASCII.GetString(bytes, valueTlv.ContentOffset, valueTlv.Length)),
            _ when (valueTlv.Tag & 0x20) == 0 => new SnmpV1RawValue(
                valueTlv.Tag,
                bytes.AsSpan(valueTlv.ContentOffset, valueTlv.Length)),
            _ => throw new InvalidDataException("The SNMP value uses an unsupported constructed tag.")
        };
        return new SnmpV1Variable(oid, value);
    }

    private static byte[] EncodeOid(string oid)
    {
        var components = SnmpObjectIdentifier.Parse(SnmpObjectIdentifier.Normalize(oid));
        var content = new List<byte>();
        EncodeBase128(checked((components[0] * 40) + components[1]), content);
        for (var index = 2; index < components.Length; index++)
        {
            EncodeBase128(components[index], content);
        }

        return content.ToArray();
    }

    private static string DecodeOid(byte[] bytes, BerTlv tlv)
    {
        RequireTag(tlv, 0x06, "object identifier");
        if (tlv.Length == 0)
        {
            throw new InvalidDataException("The SNMP object identifier is empty.");
        }

        var content = bytes.AsSpan(tlv.ContentOffset, tlv.Length);
        var values = new List<ulong>();
        var offset = 0;
        while (offset < content.Length)
        {
            ulong value = 0;
            var firstOctet = true;
            while (true)
            {
                if (offset >= content.Length)
                {
                    throw new InvalidDataException("The SNMP object identifier is truncated.");
                }

                var octet = content[offset++];
                if (firstOctet && octet == 0x80)
                {
                    throw new InvalidDataException("The SNMP object identifier is not minimally encoded.");
                }

                firstOctet = false;
                if (value > (ulong.MaxValue >> 7))
                {
                    throw new InvalidDataException("The SNMP object identifier component is too large.");
                }

                value = (value << 7) | (uint)(octet & 0x7F);
                if ((octet & 0x80) == 0)
                {
                    break;
                }
            }

            values.Add(value);
        }

        var firstCombined = values[0];
        ulong first;
        ulong second;
        if (firstCombined < 40)
        {
            first = 0;
            second = firstCombined;
        }
        else if (firstCombined < 80)
        {
            first = 1;
            second = firstCombined - 40;
        }
        else
        {
            first = 2;
            second = firstCombined - 80;
        }

        var components = new[] { first, second }.Concat(values.Skip(1));
        return string.Join(
            ".",
            components.Select(component => component.ToString(CultureInfo.InvariantCulture)));
    }

    private static void EncodeBase128(ulong value, List<byte> output)
    {
        Span<byte> encoded = stackalloc byte[10];
        var index = encoded.Length;
        do
        {
            encoded[--index] = (byte)(value & 0x7F);
            value >>= 7;
        }
        while (value != 0);

        for (; index < encoded.Length - 1; index++)
        {
            output.Add((byte)(encoded[index] | 0x80));
        }

        output.Add(encoded[^1]);
    }

    private static byte[] EncodeInteger(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        var start = 0;
        while (start < bytes.Length - 1 &&
               ((bytes[start] == 0x00 && (bytes[start + 1] & 0x80) == 0) ||
                (bytes[start] == 0xFF && (bytes[start + 1] & 0x80) != 0)))
        {
            start++;
        }

        return bytes[start..].ToArray();
    }

    private static int ReadInteger(byte[] bytes, BerTlv tlv)
    {
        RequireTag(tlv, 0x02, "integer");
        if (tlv.Length is < 1 or > 4)
        {
            throw new InvalidDataException("The SNMP integer size is invalid.");
        }

        if (tlv.Length > 1 &&
            ((bytes[tlv.ContentOffset] == 0x00 &&
              (bytes[tlv.ContentOffset + 1] & 0x80) == 0) ||
             (bytes[tlv.ContentOffset] == 0xFF &&
              (bytes[tlv.ContentOffset + 1] & 0x80) != 0)))
        {
            throw new InvalidDataException("The SNMP integer is not minimally encoded.");
        }

        long value = (bytes[tlv.ContentOffset] & 0x80) == 0 ? 0 : -1;
        for (var index = 0; index < tlv.Length; index++)
        {
            value = (value << 8) | bytes[tlv.ContentOffset + index];
        }

        return checked((int)value);
    }

    private static byte[] Wrap(byte tag, params byte[][] parts)
    {
        var contentLength = parts.Sum(part => part.Length);
        var length = EncodeLength(contentLength);
        var result = new byte[1 + length.Length + contentLength];
        result[0] = tag;
        length.CopyTo(result, 1);
        var offset = 1 + length.Length;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] EncodeLength(int length)
    {
        if (length < 0x80)
        {
            return [(byte)length];
        }

        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, length);
        var start = 0;
        while (bytes[start] == 0)
        {
            start++;
        }

        var result = new byte[1 + bytes.Length - start];
        result[0] = (byte)(0x80 | (bytes.Length - start));
        bytes[start..].CopyTo(result.AsSpan(1));
        return result;
    }

    private static BerTlv ReadTlv(byte[] bytes, ref int offset, int boundary)
    {
        if (offset >= boundary)
        {
            throw new InvalidDataException("The BER value is truncated before its tag.");
        }

        var tag = bytes[offset++];
        if ((tag & 0x1F) == 0x1F)
        {
            throw new InvalidDataException("High-tag-number BER values are unsupported.");
        }

        if (offset >= boundary)
        {
            throw new InvalidDataException("The BER value is truncated before its length.");
        }

        var firstLength = bytes[offset++];
        int length;
        if ((firstLength & 0x80) == 0)
        {
            length = firstLength;
        }
        else
        {
            var lengthOctets = firstLength & 0x7F;
            if (lengthOctets is 0 or > 4 || offset + lengthOctets > boundary)
            {
                throw new InvalidDataException("The BER length is unsupported or truncated.");
            }

            if (bytes[offset] == 0)
            {
                throw new InvalidDataException("The BER length is not minimally encoded.");
            }

            uint lengthValue = 0;
            for (var index = 0; index < lengthOctets; index++)
            {
                lengthValue = (lengthValue << 8) | bytes[offset++];
            }

            if (lengthValue > int.MaxValue)
            {
                throw new InvalidDataException("The BER length is too large.");
            }

            length = (int)lengthValue;

            if (length < 0x80)
            {
                throw new InvalidDataException("The BER length should use short form.");
            }
        }

        if (length < 0 || length > boundary - offset)
        {
            throw new InvalidDataException("The BER value exceeds its parent boundary.");
        }

        var contentOffset = offset;
        offset += length;
        return new BerTlv(tag, contentOffset, length, offset);
    }

    private static void RequireTag(BerTlv tlv, byte expected, string name)
    {
        if (tlv.Tag != expected)
        {
            throw new InvalidDataException($"The SNMP {name} tag is invalid.");
        }
    }

    private static void RequireAtEnd(int offset, int boundary, string name)
    {
        if (offset != boundary)
        {
            throw new InvalidDataException($"The {name} contains trailing data.");
        }
    }

    private static void ValidateCommunity(ReadOnlySpan<byte> community)
    {
        if (community.IsEmpty || community.Length > 255)
        {
            throw new ArgumentException("The SNMP community length must be between 1 and 255 bytes.");
        }
    }

    private readonly record struct BerTlv(byte Tag, int ContentOffset, int Length, int EndOffset);
}

public sealed class SnmpV1AgentException : IOException
{
    public SnmpV1AgentException(int errorStatus, int errorIndex)
        : base($"The SNMP agent returned error status {errorStatus} at index {errorIndex}.")
    {
        ErrorStatus = errorStatus;
        ErrorIndex = errorIndex;
    }

    public int ErrorStatus { get; }

    public int ErrorIndex { get; }
}
