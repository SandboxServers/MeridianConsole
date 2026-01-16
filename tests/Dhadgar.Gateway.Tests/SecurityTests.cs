using System.IO;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Dhadgar.Gateway.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class SecurityTests
{
    [Fact]
    public async Task SpoofedTenantIdHeader_IsStripped_AndReplacedWithJwtClaim()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-123";
        context.Request.Headers["X-Tenant-Id"] = "spoofed-tenant";

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("org_id", "real-tenant-from-jwt")
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("real-tenant-from-jwt", context.Request.Headers["X-Tenant-Id"].ToString());
        Assert.NotEqual("spoofed-tenant", context.Request.Headers["X-Tenant-Id"].ToString());
    }

    [Fact]
    public async Task SpoofedUserIdHeader_IsStripped_AndReplacedWithJwtClaim()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-456";
        context.Request.Headers["X-User-Id"] = "spoofed-user";

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", "real-user-from-jwt")
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("real-user-from-jwt", context.Request.Headers["X-User-Id"].ToString());
        Assert.NotEqual("spoofed-user", context.Request.Headers["X-User-Id"].ToString());
    }

    [Fact]
    public async Task SpoofedRealIpHeader_IsStripped()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-789";
        context.Request.Headers["X-Real-IP"] = "1.2.3.4";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        // X-Real-IP should be set from RemoteIpAddress, not the spoofed header
        Assert.Equal("192.168.1.100", context.Request.Headers["X-Real-IP"].ToString());
        Assert.NotEqual("1.2.3.4", context.Request.Headers["X-Real-IP"].ToString());
    }

    [Fact]
    public async Task SpoofedRolesHeader_IsStripped()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-abc";
        context.Request.Headers["X-Roles"] = "Admin,SuperUser";

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Role, "Operator")
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("User,Operator", context.Request.Headers["X-Roles"].ToString());
        Assert.DoesNotContain("Admin", context.Request.Headers["X-Roles"].ToString());
        Assert.DoesNotContain("SuperUser", context.Request.Headers["X-Roles"].ToString());
    }

    [Fact]
    public async Task SpoofedAgentIdHeader_IsStripped()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-def";
        context.Request.Headers["X-Agent-Id"] = "spoofed-agent";

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("agent_id", "real-agent-from-jwt")
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("real-agent-from-jwt", context.Request.Headers["X-Agent-Id"].ToString());
        Assert.NotEqual("spoofed-agent", context.Request.Headers["X-Agent-Id"].ToString());
    }

    [Fact]
    public async Task AllSecurityHeaders_AreStripped_WhenPresent()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-ghi";

        // Set all spoofable headers
        context.Request.Headers["X-Tenant-Id"] = "spoofed";
        context.Request.Headers["X-User-Id"] = "spoofed";
        context.Request.Headers["X-Client-Type"] = "spoofed";
        context.Request.Headers["X-Agent-Id"] = "spoofed";
        context.Request.Headers["X-Roles"] = "spoofed";
        context.Request.Headers["X-Real-IP"] = "1.1.1.1";

        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        // Without JWT claims, headers should be empty (stripped)
        Assert.Empty(context.Request.Headers["X-Tenant-Id"].ToString());
        Assert.Empty(context.Request.Headers["X-User-Id"].ToString());
        Assert.Empty(context.Request.Headers["X-Client-Type"].ToString());
        Assert.Empty(context.Request.Headers["X-Agent-Id"].ToString());
        Assert.Empty(context.Request.Headers["X-Roles"].ToString());

        // X-Real-IP should be set from RemoteIpAddress
        Assert.Equal("10.0.0.1", context.Request.Headers["X-Real-IP"].ToString());
    }

    [Fact]
    public async Task UnauthenticatedRequest_HasHeadersStripped_ButIpSet()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-jkl";
        context.Request.Headers["X-Tenant-Id"] = "attack-tenant";
        context.Request.Headers["X-User-Id"] = "attack-user";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.50");

        // No claims set - anonymous request
        context.User = new ClaimsPrincipal();

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        // Security headers should be stripped (empty for unauthenticated)
        Assert.Empty(context.Request.Headers["X-Tenant-Id"].ToString());
        Assert.Empty(context.Request.Headers["X-User-Id"].ToString());

        // X-Real-IP should still be set from RemoteIpAddress
        Assert.Equal("203.0.113.50", context.Request.Headers["X-Real-IP"].ToString());
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        return context;
    }

    private static Task WriteResponseAsync(HttpContext context)
    {
        return context.Response.WriteAsync("ok");
    }
}
