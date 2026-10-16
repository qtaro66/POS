using System.Globalization;

namespace Pos.Printing.Snmp;

internal static class SnmpObjectIdentifier
{
    public static string Normalize(string value)
    {
        var components = Parse(value);
        var normalized = string.Join(
            ".",
            components.Select(component => component.ToString(CultureInfo.InvariantCulture)));
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException("The OID must use canonical decimal form.", nameof(value));
        }

        return normalized;
    }

    public static ulong[] Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The OID cannot be empty.", nameof(value));
        }

        var parts = value.Split('.');
        if (parts.Length < 2)
        {
            throw new ArgumentException("The OID must contain at least two components.", nameof(value));
        }

        var components = new ulong[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!ulong.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out components[index]))
            {
                throw new ArgumentException("The OID contains an invalid component.", nameof(value));
            }
        }

        if (components[0] > 2 || (components[0] < 2 && components[1] > 39))
        {
            throw new ArgumentException("The OID has invalid first components.", nameof(value));
        }

        if (components[0] == 2 && components[1] > ulong.MaxValue - 80)
        {
            throw new ArgumentException("The OID second component is too large.", nameof(value));
        }

        return components;
    }

    public static int Compare(string left, string right)
    {
        var leftParts = Parse(left);
        var rightParts = Parse(right);
        var sharedLength = Math.Min(leftParts.Length, rightParts.Length);
        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = leftParts[index].CompareTo(rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    public static bool IsDescendant(string oid, string subtree)
    {
        var oidParts = Parse(oid);
        var subtreeParts = Parse(subtree);
        return oidParts.Length > subtreeParts.Length &&
            oidParts.AsSpan(0, subtreeParts.Length).SequenceEqual(subtreeParts);
    }
}
