using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using Dhadgar.Gateway.Services;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class RateLimitPartitionKeysTests
{
    [Fact]
    public void Tenant_AuthenticatedPrincipalWithOrgId_ReturnsTenantOrgKey()
    {
        var user = CreatePrincipal(authenticated: true, ("org_id", "org-123"));

        var key = RateLimitPartitionKeys.Tenant(user, IPAddress.Parse("203.0.113.60"));

        Assert.Equal("tenant-org:org-123", key);
    }

    [Fact]
    public void Tenant_UnauthenticatedPrincipalWithOrgId_IgnoresClaimAndReturnsIpKey()
    {
        var user = CreatePrincipal(authenticated: false, ("org_id", "org-123"));

        var key = RateLimitPartitionKeys.Tenant(user, IPAddress.Parse("203.0.113.61"));

        Assert.Equal("tenant-ip:203.0.113.61", key);
    }

    [Fact]
    public void Tenant_NullUser_Ipv6_ReturnsIpKeyWith64Prefix()
    {
        var key = RateLimitPartitionKeys.Tenant(null, IPAddress.Parse("2001:db8:1:2:3:4:5:6"));

        Assert.Equal("tenant-ip:2001:db8:1:2::", key);
    }

    [Fact]
    public void Tenant_WhitespaceOrgId_ReturnsIpKey()
    {
        var user = CreatePrincipal(authenticated: true, ("org_id", "   "));

        var key = RateLimitPartitionKeys.Tenant(user, IPAddress.Parse("203.0.113.62"));

        Assert.Equal("tenant-ip:203.0.113.62", key);
    }

    [Fact]
    public void Tenant_NullIp_ReturnsUnknownIpKey()
    {
        var key = RateLimitPartitionKeys.Tenant(null, null);

        Assert.Equal("tenant-ip:unknown", key);
    }

    private static ClaimsPrincipal CreatePrincipal(bool authenticated, params (string Type, string Value)[] claims)
    {
        var claimList = new List<Claim>();
        foreach (var (type, value) in claims)
        {
            claimList.Add(new Claim(type, value));
        }

        // An identity without an authentication type reports IsAuthenticated = false.
        var identity = authenticated
            ? new ClaimsIdentity(claimList, "Test")
            : new ClaimsIdentity(claimList);

        return new ClaimsPrincipal(identity);
    }
}
