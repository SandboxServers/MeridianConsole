using System;
using System.Net;
using Dhadgar.Gateway.Services;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class RateLimitingBehaviorTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("Default", true)]
    [InlineData("default", true)]
    [InlineData("Auth", false)]
    [InlineData("PerTenant", false)]
    [InlineData("PerAgent", false)]
    public void RouteRateLimiting_UsesGlobal_OnlyForRoutesWithoutPolicy(string? routePolicy, bool expected) =>
        Assert.Equal(expected, RouteRateLimiting.UsesGlobal(routePolicy));

    [Theory]
    [InlineData("192.168.1.1", "192.168.1.1")]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData("172.16.0.1", "172.16.0.1")]
    public void IPv4Address_UsesFullAddress_ForRateLimitPartition(string inputIp, string expectedPartition)
    {
        var ip = IPAddress.Parse(inputIp);
        var partitionKey = GetRateLimitPartitionKey(ip);

        Assert.Equal(expectedPartition, partitionKey);
    }

    [Theory]
    [InlineData("2001:db8:85a3::8a2e:370:7334", "2001:db8:85a3::")] // Standard IPv6
    [InlineData("2001:db8:85a3:1234:5678:9abc:def0:1234", "2001:db8:85a3:1234::")] // Different /64
    [InlineData("2001:db8::", "2001:db8::")] // Already a /64 prefix
    [InlineData("fe80::1", "unknown-linklocal")] // Link-local
    public void IPv6Address_Uses64Prefix_ForRateLimitPartition(string inputIp, string expectedPartition)
    {
        var ip = IPAddress.Parse(inputIp);
        var partitionKey = GetRateLimitPartitionKey(ip);

        Assert.Equal(expectedPartition, partitionKey);
    }

    [Theory]
    [InlineData("::ffff:192.168.1.1", "::ffff:192.168.1.1")] // IPv4-mapped IPv6
    [InlineData("::ffff:10.0.0.1", "::ffff:10.0.0.1")] // IPv4-mapped IPv6
    public void IPv4MappedToIPv6_UsesFullAddress_ForRateLimitPartition(string inputIp, string expectedPartition)
    {
        var ip = IPAddress.Parse(inputIp);
        var partitionKey = GetRateLimitPartitionKey(ip);

        Assert.Equal(expectedPartition, partitionKey);
    }

    [Fact]
    public void NullIpAddress_ReturnsUnknown_ForRateLimitPartition()
    {
        var partitionKey = GetRateLimitPartitionKey(null);

        Assert.Equal("unknown", partitionKey);
    }

    [Fact]
    public void IPv6AddressesInSameSubnet_HaveSamePartitionKey()
    {
        // These two IPs are in the same /64 subnet (2001:db8:85a3:1234::/64)
        var ip1 = IPAddress.Parse("2001:db8:85a3:1234::1");
        var ip2 = IPAddress.Parse("2001:db8:85a3:1234:ffff:ffff:ffff:ffff");

        var partitionKey1 = GetRateLimitPartitionKey(ip1);
        var partitionKey2 = GetRateLimitPartitionKey(ip2);

        Assert.Equal(partitionKey1, partitionKey2);
    }

    [Fact]
    public void IPv6AddressesInDifferentSubnets_HaveDifferentPartitionKeys()
    {
        // These two IPs are in different /64 subnets
        var ip1 = IPAddress.Parse("2001:db8:85a3:1234::1");
        var ip2 = IPAddress.Parse("2001:db8:85a3:5678::1");

        var partitionKey1 = GetRateLimitPartitionKey(ip1);
        var partitionKey2 = GetRateLimitPartitionKey(ip2);

        Assert.NotEqual(partitionKey1, partitionKey2);
    }

    [Fact]
    public void IPv4AndIPv6_HaveDifferentPartitionKeys()
    {
        var ipv4 = IPAddress.Parse("192.168.1.1");
        var ipv6 = IPAddress.Parse("2001:db8:85a3::1");

        var partitionKeyV4 = GetRateLimitPartitionKey(ipv4);
        var partitionKeyV6 = GetRateLimitPartitionKey(ipv6);

        Assert.NotEqual(partitionKeyV4, partitionKeyV6);
    }

    [Fact]
    public void LinkLocalIpv6_ReturnsLinkLocalBucket_ForRateLimitPartition()
    {
        Assert.Equal("unknown-linklocal", GetRateLimitPartitionKey(IPAddress.Parse("fe80::1")));
    }

    private static string GetRateLimitPartitionKey(IPAddress? ip)
    {
        return RateLimitPartitionKeys.ClientIp(ip);
    }
}
