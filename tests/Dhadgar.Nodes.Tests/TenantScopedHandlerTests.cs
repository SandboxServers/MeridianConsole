using System.Security.Claims;
using Dhadgar.Nodes.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dhadgar.Nodes.Tests;

public sealed class TenantScopedHandlerTests
{
    private static readonly Guid TestOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOrgId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (TenantScopedHandler Handler, IHttpContextAccessor Accessor) CreateHandler()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var handler = new TenantScopedHandler(accessor, NullLogger<TenantScopedHandler>.Instance);
        return (handler, accessor);
    }

    private static DefaultHttpContext CreateHttpContext(string? routeOrgId, ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext();

        if (routeOrgId is not null)
        {
            httpContext.Request.RouteValues = new RouteValueDictionary
            {
                ["organizationId"] = routeOrgId
            };
        }

        if (user is not null)
        {
            httpContext.User = user;
        }

        return httpContext;
    }

    private static ClaimsPrincipal CreateUser(string? orgId)
    {
        var claims = new List<Claim>
        {
            new("sub", "user-123")
        };

        if (orgId is not null)
        {
            claims.Add(new Claim("org_id", orgId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task HandleRequirementAsync_MatchingOrgIds_Succeeds()
    {
        // Arrange
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext(TestOrgId.ToString(), CreateUser(TestOrgId.ToString()));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_NoHttpContext_Fails()
    {
        // Arrange
        var (handler, accessor) = CreateHandler();
        accessor.HttpContext.Returns((HttpContext?)null);

        var requirement = new TenantScopedRequirement();
        var user = CreateUser(TestOrgId.ToString());
        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_NoOrganizationIdInRoute_Succeeds()
    {
        // Arrange - route without organizationId should succeed (handler doesn't apply)
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext(null, CreateUser(TestOrgId.ToString()));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_InvalidOrganizationIdInRoute_Fails()
    {
        // Arrange
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext("not-a-guid", CreateUser(TestOrgId.ToString()));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_UserWithNoOrgClaim_Fails()
    {
        // Arrange
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext(TestOrgId.ToString(), CreateUser(null));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_UserWithInvalidOrgClaimFormat_Fails()
    {
        // Arrange
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext(TestOrgId.ToString(), CreateUser("not-a-guid"));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_MismatchedOrgIds_Fails()
    {
        // Arrange - user org doesn't match route org
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext(TestOrgId.ToString(), CreateUser(OtherOrgId.ToString()));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_EmptyOrgClaim_Fails()
    {
        // Arrange
        var (handler, accessor) = CreateHandler();
        var httpContext = CreateHttpContext(TestOrgId.ToString(), CreateUser(""));
        accessor.HttpContext.Returns(httpContext);

        var requirement = new TenantScopedRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            httpContext.User,
            resource: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }
}
