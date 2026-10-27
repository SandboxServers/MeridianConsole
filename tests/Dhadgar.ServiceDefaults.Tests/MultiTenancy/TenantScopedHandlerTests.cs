using System.Security.Claims;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.MultiTenancy;

public sealed class TenantScopedHandlerTests
{
    [Fact]
    public async Task SucceedsWhenOrgClaimMatchesRouteOrganization()
    {
        var orgId = Guid.NewGuid();
        var context = await EvaluateAsync(routeOrgId: orgId, claimOrgId: orgId.ToString());

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task FailsWhenOrgClaimDiffersFromRouteOrganization()
    {
        var context = await EvaluateAsync(
            routeOrgId: Guid.NewGuid(),
            claimOrgId: Guid.NewGuid().ToString());

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task FailsWhenOrgClaimIsMissing()
    {
        var context = await EvaluateAsync(routeOrgId: Guid.NewGuid(), claimOrgId: null);

        Assert.False(context.HasSucceeded);
    }

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(Guid routeOrgId, string? claimOrgId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["organizationId"] = routeOrgId.ToString();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (claimOrgId is not null)
        {
            claims.Add(new Claim("org_id", claimOrgId));
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var requirements = new IAuthorizationRequirement[] { new TenantScopedRequirement() };
        var context = new AuthorizationHandlerContext(requirements, user, resource: null);

        var handler = new TenantScopedHandler(accessor, NullLogger<TenantScopedHandler>.Instance);
        await handler.HandleAsync(context);
        return context;
    }
}
