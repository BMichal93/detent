using System.Net;
using System.Net.Sockets;

namespace Detent.Transport;

/// <summary>
/// Decides whether an IP address is one this tool refuses to connect to.
/// </summary>
/// <remarks>
/// The SSRF control from <c>docs/arch/security-model.md</c> §1. A target URL can
/// arrive from a config file or a registry entry rather than from the person
/// running the command, so "the user typed it" is not an argument that the
/// address is safe.
/// <para>
/// The blocklist is deliberately wider than loopback and RFC1918. Cloud metadata
/// at <c>169.254.169.254</c> is the highest-value target on the list and lives in
/// link-local, which a narrower list would miss.
/// </para>
/// </remarks>
public static class AddressGuard
{
    private static readonly Cidr[] _blocked =
    [
        // IPv4
        new("0.0.0.0", 8, "unspecified"),
        new("10.0.0.0", 8, "private"),
        new("100.64.0.0", 10, "carrier-grade NAT"),
        new("127.0.0.0", 8, "loopback"),
        new("169.254.0.0", 16, "link-local, cloud metadata"),
        new("172.16.0.0", 12, "private"),
        new("192.0.0.0", 24, "IETF protocol assignments"),
        new("192.168.0.0", 16, "private"),
        new("198.18.0.0", 15, "benchmarking"),
        new("224.0.0.0", 4, "multicast"),
        new("240.0.0.0", 4, "reserved"),

        // IPv6
        new("::", 128, "unspecified"),
        new("::1", 128, "loopback"),

        // IPv4-compatible IPv6 (::a.b.c.d). Deprecated by RFC 4291 and never a
        // legitimate target, but ::169.254.169.254 and ::7f00:1 would otherwise
        // walk straight past this list: the wrapper below does not recognise
        // this form, and the outer address matches no other range. Listed after
        // the two entries above so :: and ::1 keep their specific reasons.
        new("::", 96, "IPv4-compatible IPv6, deprecated"),
        new("fc00::", 7, "unique local"),
        new("fe80::", 10, "link-local"),
        new("ff00::", 8, "multicast"),
    ];

    /// <summary>
    /// The reason this address is refused, or <c>null</c> if it is acceptable.
    /// </summary>
    public static string? BlockReason(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Transition and mapping formats carry an IPv4 address inside an IPv6
        // one. Checking the outer form alone would let ::ffff:169.254.169.254
        // through the entire list above.
        if (Unwrap(address) is { } embedded)
        {
            return BlockReason(embedded);
        }

        foreach (var range in _blocked)
        {
            if (range.Contains(address))
            {
                return range.Reason;
            }
        }

        return null;
    }

    /// <summary>Whether this address is refused.</summary>
    public static bool IsBlocked(IPAddress address) => BlockReason(address) is not null;

    /// <summary>
    /// Extracts the IPv4 address embedded in an IPv6 one, if there is one.
    /// </summary>
    private static IPAddress? Unwrap(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();

        // 6to4 (2002::/16) embeds the IPv4 address in the next four bytes.
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return new IPAddress(bytes[2..6]);
        }

        // NAT64 well-known prefix (64:ff9b::/96) embeds it in the last four.
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B)
        {
            return new IPAddress(bytes[12..16]);
        }

        return null;
    }

    private sealed class Cidr
    {
        private readonly byte[] _network;
        private readonly int _prefixLength;

        public Cidr(string network, int prefixLength, string reason)
        {
            _network = IPAddress.Parse(network).GetAddressBytes();
            _prefixLength = prefixLength;
            Reason = reason;
        }

        public string Reason { get; }

        public bool Contains(IPAddress address)
        {
            var bytes = address.GetAddressBytes();

            if (bytes.Length != _network.Length)
            {
                return false;
            }

            var wholeBytes = _prefixLength / 8;

            for (var i = 0; i < wholeBytes; i++)
            {
                if (bytes[i] != _network[i])
                {
                    return false;
                }
            }

            var remainingBits = _prefixLength % 8;

            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (bytes[wholeBytes] & mask) == (_network[wholeBytes] & mask);
        }
    }
}
