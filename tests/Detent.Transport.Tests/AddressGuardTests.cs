using System.Net;

namespace Detent.Transport.Tests;

/// <summary>
/// The SSRF blocklist from docs/arch/security-model.md §1.
/// </summary>
public sealed class AddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("127.1.2.3", "loopback")]
    [InlineData("0.0.0.0", "unspecified")]
    [InlineData("10.1.2.3", "private")]
    [InlineData("172.16.0.1", "private")]
    [InlineData("172.31.255.255", "private")]
    [InlineData("192.168.1.1", "private")]
    [InlineData("100.64.0.1", "carrier-grade NAT")]
    [InlineData("198.18.0.1", "benchmarking")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("255.255.255.255", "reserved")]
    [InlineData("::1", "loopback")]
    [InlineData("fe80::1", "link-local")]
    [InlineData("fd00::1", "unique local")]
    [InlineData("ff02::1", "multicast")]
    public void Blocked_ranges_are_refused(string address, string reason)
        => Assert.Equal(reason, AddressGuard.BlockReason(IPAddress.Parse(address)));

    /// <summary>
    /// The highest-value target on the list. A blocklist that stops at loopback
    /// and RFC1918 misses it, which is the usual way SSRF controls fail.
    /// </summary>
    [Fact]
    public void Cloud_metadata_is_refused()
        => Assert.Equal("link-local, cloud metadata", AddressGuard.BlockReason(IPAddress.Parse("169.254.169.254")));

    /// <summary>
    /// Every IPv6 form that can carry an IPv4 address inside it. Checking only
    /// the outer form would let each of these through the whole list.
    /// </summary>
    [Theory]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("2002:a9fe:a9fe::")]
    [InlineData("64:ff9b::169.254.169.254")]
    // IPv4-compatible form. Deprecated, and the one shape the unwrapper does not
    // recognise, so it relies on ::/96 being on the list instead.
    [InlineData("::169.254.169.254")]
    [InlineData("::7f00:1")]
    [InlineData("0:0:0:0:0:0:7f00:1")]
    public void Embedded_ipv4_is_unwrapped_before_checking(string address)
        => Assert.True(AddressGuard.IsBlocked(IPAddress.Parse(address)));

    /// <summary>
    /// ::/96 sits between two /128 entries that name more specific reasons. If
    /// it were ordered before them, both would report the wrong cause.
    /// </summary>
    [Fact]
    public void Specific_ipv6_reasons_survive_the_wider_range()
    {
        Assert.Equal("unspecified", AddressGuard.BlockReason(IPAddress.IPv6Any));
        Assert.Equal("loopback", AddressGuard.BlockReason(IPAddress.IPv6Loopback));
    }

    /// <summary>
    /// v4-mapped (::ffff:0:0/96) is a different range from v4-compatible (::/96)
    /// and must not be swallowed by it.
    /// </summary>
    [Fact]
    public void Mapped_and_compatible_ranges_stay_distinct()
    {
        Assert.Equal("link-local, cloud metadata", AddressGuard.BlockReason(IPAddress.Parse("::ffff:169.254.169.254")));
        Assert.Equal("IPv4-compatible IPv6, deprecated", AddressGuard.BlockReason(IPAddress.Parse("::169.254.169.254")));
        Assert.Null(AddressGuard.BlockReason(IPAddress.Parse("::ffff:93.184.216.34")));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void Routable_addresses_are_allowed(string address)
        => Assert.Null(AddressGuard.BlockReason(IPAddress.Parse(address)));

    /// <summary>
    /// 172.16.0.0/12 ends at 172.31.255.255. An off-by-one in the mask would
    /// either block a public range or leak a private one.
    /// </summary>
    [Fact]
    public void Prefix_boundaries_are_exact()
    {
        Assert.True(AddressGuard.IsBlocked(IPAddress.Parse("172.31.255.255")));
        Assert.False(AddressGuard.IsBlocked(IPAddress.Parse("172.15.255.255")));
        Assert.False(AddressGuard.IsBlocked(IPAddress.Parse("172.32.0.0")));
    }
}
