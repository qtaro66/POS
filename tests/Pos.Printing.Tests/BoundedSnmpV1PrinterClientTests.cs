using System.Buffers.Binary;
using System.Text;
using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Printing.Tests;

public sealed class BoundedSnmpV1PrinterClientTests
{
    private const string SystemDescriptionOid = "1.3.6.1.2.1.1.1.0";
    private const string AlertSubtreeOid = "1.3.6.1.2.1.43.18.1.1";

    [Fact]
    public async Task Get_encodes_the_Rfc1157_request_and_decodes_one_response_variable()
    {
        var transport = new FakeTransport(
            Convert.FromHexString(
                "302A02010004067075626C6963A21D0201010201000201003012301006082B06010201010100040454657374"));
        var client = Client(transport, 1);

        var variable = await client.GetAsync(Profile(), SystemDescriptionOid, CancellationToken.None);

        Assert.Equal(SystemDescriptionOid, variable.Oid);
        Assert.Equal("Test", Assert.IsType<SnmpV1OctetStringValue>(variable.Value).Value);
        Assert.Equal(
            "302602010004067075626C6963A019020101020100020100300E300C06082B060102010101000500",
            Convert.ToHexString(Assert.Single(transport.Requests)));
    }

    [Fact]
    public async Task Get_decodes_negative_integer_values()
    {
        var transport = new FakeTransport(Response(
            1,
            "1.3.6.1.2.1.43.8.2.1.10.1.1",
            new SnmpV1IntegerValue(-3)));

        var variable = await Client(transport, 1).GetAsync(
            Profile(),
            "1.3.6.1.2.1.43.8.2.1.10.1.1",
            CancellationToken.None);

        Assert.Equal(-3, Assert.IsType<SnmpV1IntegerValue>(variable.Value).Value);
    }

    [Fact]
    public async Task Get_supports_long_form_lengths_and_multi_octet_oid_components()
    {
        const string oid = "1.3.6.1.2.1.43.18.1.1.8.1.128";
        var description = new string('A', 130);
        var transport = new FakeTransport(Response(
            1,
            oid,
            new SnmpV1OctetStringValue(description)));

        var variable = await Client(transport, 1).GetAsync(
            Profile(),
            oid,
            CancellationToken.None);

        Assert.Equal(oid, variable.Oid);
        Assert.Equal(
            description,
            Assert.IsType<SnmpV1OctetStringValue>(variable.Value).Value);
    }

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task Get_rejects_mismatched_or_malformed_responses(byte[] response)
    {
        var client = Client(new FakeTransport(response), 1);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() =>
            client.GetAsync(Profile(), SystemDescriptionOid, CancellationToken.None));
    }

    [Fact]
    public async Task Agent_error_preserves_status_and_index()
    {
        var response = Convert.FromHexString(
            "302A02010004067075626C6963A21D0201010201020201013012301006082B06010201010100040454657374");

        var exception = await Assert.ThrowsAsync<SnmpV1AgentException>(() =>
            Client(new FakeTransport(response), 1)
                .GetAsync(Profile(), SystemDescriptionOid, CancellationToken.None));

        Assert.Equal(2, exception.ErrorStatus);
        Assert.Equal(1, exception.ErrorIndex);
    }

    [Fact]
    public async Task Walk_uses_get_next_until_the_response_leaves_the_subtree()
    {
        var transport = new FakeTransport(
            Response(
                11,
                $"{AlertSubtreeOid}.7.1.42",
                new SnmpV1IntegerValue(808)),
            Response(
                12,
                $"{AlertSubtreeOid}.8.1.42",
                new SnmpV1OctetStringValue("Roll Paper End")),
            Response(
                13,
                "1.3.6.1.2.1.43.18.2.0",
                new SnmpV1IntegerValue(0)));

        var result = await Client(transport, 11, 12, 13).WalkAsync(
            Profile(),
            AlertSubtreeOid,
            10,
            CancellationToken.None);

        Assert.True(result.CompletedSubtree);
        Assert.Equal(2, result.Variables.Count);
        Assert.All(transport.Requests, request => Assert.Contains((byte)0xA1, request));
    }

    [Fact]
    public async Task Walk_reports_incomplete_when_the_bound_is_reached()
    {
        var transport = new FakeTransport(Response(
            1,
            $"{AlertSubtreeOid}.7.1.42",
            new SnmpV1IntegerValue(808)));

        var result = await Client(transport, 1).WalkAsync(
            Profile(),
            AlertSubtreeOid,
            1,
            CancellationToken.None);

        Assert.False(result.CompletedSubtree);
        Assert.Single(result.Variables);
    }

    [Fact]
    public async Task Walk_preserves_uninterpreted_primitive_values()
    {
        var transport = new FakeTransport(
            Response(
                1,
                $"{AlertSubtreeOid}.9.1.42",
                new SnmpV1RawValue(0x43, [0, 0, 0, 1])),
            Response(
                2,
                "1.3.6.1.2.1.43.18.2.0",
                new SnmpV1IntegerValue(0)));

        var result = await Client(transport, 1, 2).WalkAsync(
            Profile(),
            AlertSubtreeOid,
            10,
            CancellationToken.None);

        var value = Assert.IsType<SnmpV1RawValue>(Assert.Single(result.Variables).Value);
        Assert.Equal(0x43, value.Tag);
        Assert.Equal(new byte[] { 0, 0, 0, 1 }, value.Bytes.ToArray());
    }

    [Theory]
    [InlineData("1.3.6.1.2.1.43.18.1.1")]
    [InlineData("1.3.6.1.2.1.43.18.1.0")]
    public async Task Walk_rejects_non_advancing_or_regressing_responses(string responseOid)
    {
        var transport = new FakeTransport(Response(
            1,
            responseOid,
            new SnmpV1IntegerValue(0)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client(transport, 1).WalkAsync(
                Profile(),
                AlertSubtreeOid,
                10,
                CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public async Task Walk_rejects_an_unsafe_bound_before_resolving_secrets(int bound)
    {
        var community = new FakeCommunityProvider("public"u8.ToArray());
        var client = new BoundedSnmpV1PrinterClient(
            community,
            new FakeTransport(),
            new FixedRequestIdSource(1));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.WalkAsync(Profile(), AlertSubtreeOid, bound, CancellationToken.None));

        Assert.Equal(0, community.CallCount);
    }

    [Fact]
    public async Task Invalid_request_id_is_rejected_before_transport_access()
    {
        var transport = new FakeTransport();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client(transport, 0).GetAsync(Profile(), SystemDescriptionOid, CancellationToken.None));

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Transport_failure_is_propagated_without_automatic_retry()
    {
        var transport = new ThrowingTransport(new IOException("simulated UDP failure"));
        var client = new BoundedSnmpV1PrinterClient(
            new FakeCommunityProvider("public"u8.ToArray()),
            transport,
            new FixedRequestIdSource(1));

        await Assert.ThrowsAsync<IOException>(() =>
            client.GetAsync(Profile(), SystemDescriptionOid, CancellationToken.None));

        Assert.Equal(1, transport.CallCount);
    }

    [Theory]
    [InlineData("1.3.6.01")]
    [InlineData("3.1.6")]
    [InlineData("1.40.6")]
    public async Task Noncanonical_or_invalid_oids_are_rejected_before_secret_access(string oid)
    {
        var community = new FakeCommunityProvider("public"u8.ToArray());
        var client = new BoundedSnmpV1PrinterClient(
            community,
            new FakeTransport(),
            new FixedRequestIdSource(1));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAsync(Profile(), oid, CancellationToken.None));

        Assert.Equal(0, community.CallCount);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_secret_and_transport_boundaries()
    {
        using var source = new CancellationTokenSource();
        var community = new FakeCommunityProvider("public"u8.ToArray());
        var transport = new FakeTransport(Response(
            1,
            SystemDescriptionOid,
            new SnmpV1OctetStringValue("Test")));
        var client = new BoundedSnmpV1PrinterClient(
            community,
            transport,
            new FixedRequestIdSource(1));

        await client.GetAsync(Profile(), SystemDescriptionOid, source.Token);

        Assert.Equal(source.Token, community.LastCancellationToken);
        Assert.Equal(source.Token, transport.LastCancellationToken);
    }

    public static TheoryData<byte[]> InvalidResponses
    {
        get
        {
            var valid = Convert.FromHexString(
                "302A02010004067075626C6963A21D0201010201000201003012301006082B06010201010100040454657374");
            var wrongCommunity = valid.ToArray();
            wrongCommunity[10] = (byte)'x';
            var wrongRequestId = valid.ToArray();
            wrongRequestId[17] = 2;
            var trailing = valid.Concat(new byte[] { 0 }).ToArray();
            var truncated = valid[..^1];
            return new TheoryData<byte[]>
            {
                wrongCommunity,
                wrongRequestId,
                trailing,
                truncated
            };
        }
    }

    private static BoundedSnmpV1PrinterClient Client(
        FakeTransport transport,
        params int[] requestIds) =>
        new(
            new FakeCommunityProvider("public"u8.ToArray()),
            transport,
            new FixedRequestIdSource(requestIds));

    private static byte[] Response(int requestId, string oid, SnmpV1Value value)
    {
        var valueTlv = value switch
        {
            SnmpV1IntegerValue integer => Tlv(0x02, Integer(integer.Value)),
            SnmpV1OctetStringValue text => Tlv(0x04, Encoding.ASCII.GetBytes(text.Value)),
            SnmpV1RawValue raw => Tlv(raw.Tag, raw.Bytes.ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        var variableBinding = Tlv(0x30, Tlv(0x06, Oid(oid)), valueTlv);
        var pdu = Tlv(
            0xA2,
            Tlv(0x02, Integer(requestId)),
            Tlv(0x02, [0]),
            Tlv(0x02, [0]),
            Tlv(0x30, variableBinding));
        return Tlv(0x30, Tlv(0x02, [0]), Tlv(0x04, "public"u8.ToArray()), pdu);
    }

    private static byte[] Tlv(byte tag, params byte[][] parts)
    {
        var contentLength = parts.Sum(part => part.Length);
        Assert.True(contentLength <= 255, "The independent fixture encoder supports one length octet.");
        var length = contentLength < 128
            ? new[] { (byte)contentLength }
            : new[] { (byte)0x81, (byte)contentLength };
        return new[] { tag }
            .Concat(length)
            .Concat(parts.SelectMany(part => part))
            .ToArray();
    }

    private static byte[] Integer(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        var start = 0;
        while (start < 3 &&
               ((bytes[start] == 0 && (bytes[start + 1] & 0x80) == 0) ||
                (bytes[start] == 0xFF && (bytes[start + 1] & 0x80) != 0)))
        {
            start++;
        }

        return bytes[start..].ToArray();
    }

    private static byte[] Oid(string oid)
    {
        var components = oid.Split('.').Select(ulong.Parse).ToArray();
        var result = new List<byte> { checked((byte)((components[0] * 40) + components[1])) };
        Span<byte> bytes = stackalloc byte[10];
        foreach (var component in components.Skip(2))
        {
            bytes.Clear();
            var start = bytes.Length;
            var remaining = component;
            do
            {
                bytes[--start] = (byte)(remaining & 0x7F);
                remaining >>= 7;
            }
            while (remaining != 0);

            for (var index = start; index < bytes.Length - 1; index++)
            {
                result.Add((byte)(bytes[index] | 0x80));
            }

            result.Add(bytes[^1]);
        }

        return result.ToArray();
    }

    private static PrinterProfile Profile() =>
        new(
            "PRINTER-01",
            "ครัวร้อน",
            "BRANCH-01",
            "KITCHEN-HOT",
            "printer-hot.branch.test",
            9100,
            PrinterProtocol.RawTcpEscPos,
            EpsonTmT82xM352AConditionEvaluator.ProfileId,
            "Epson",
            "TM-T82X",
            "M352A",
            "13.07 ESC/POS",
            80,
            203,
            576,
            PrinterRenderMode.BitmapRaster,
            PrinterThaiEncodingMode.UnicodeRaster,
            PrinterStatusMode.SnmpPrinterMib,
            false,
            "1D5601",
            null,
            "snmp-printer-mib-v1",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(1));

    private sealed class FakeCommunityProvider(byte[] community) : ISnmpV1CommunityProvider
    {
        public int CallCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> GetCommunityAsync(
            PrinterProfile profile,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCancellationToken = cancellationToken;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(community);
        }
    }

    private sealed class FakeTransport(params byte[][] responses) : ISnmpV1DatagramTransport
    {
        private readonly Queue<byte[]> _responses = new(responses);

        public List<byte[]> Requests { get; } = [];

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ReadOnlyMemory<byte>> ExchangeAsync(
            PrinterProfile profile,
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.ToArray());
            LastCancellationToken = cancellationToken;
            return Task.FromResult<ReadOnlyMemory<byte>>(_responses.Dequeue());
        }
    }

    private sealed class FixedRequestIdSource(params int[] values) : ISnmpV1RequestIdSource
    {
        private readonly Queue<int> _values = new(values);

        public int Next() => _values.Dequeue();
    }

    private sealed class ThrowingTransport(Exception failure) : ISnmpV1DatagramTransport
    {
        public int CallCount { get; private set; }

        public Task<ReadOnlyMemory<byte>> ExchangeAsync(
            PrinterProfile profile,
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<ReadOnlyMemory<byte>>(failure);
        }
    }
}
