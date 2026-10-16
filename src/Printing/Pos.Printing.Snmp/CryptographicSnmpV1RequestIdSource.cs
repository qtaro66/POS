using System.Security.Cryptography;

namespace Pos.Printing.Snmp;

public sealed class CryptographicSnmpV1RequestIdSource : ISnmpV1RequestIdSource
{
    public int Next() => RandomNumberGenerator.GetInt32(1, int.MaxValue);
}
