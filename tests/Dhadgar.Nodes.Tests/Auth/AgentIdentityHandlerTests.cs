using System.Security.Claims;
using Dhadgar.Nodes.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Tests.Auth;

public sealed class AgentIdentityHandlerTests
{
    private const string BearerScheme = "Bearer";

    [Fact]
    public async Task MtlsIdentityWithNodeId_Succeeds()
    {
        var principal = Principal(MtlsMiddleware.AuthenticationType, Guid.NewGuid());

        var context = await HandleAsync(principal, mtlsEnabled: true);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task BearerIdentityWithNodeId_FailsWhenMtlsEnabled()
    {
        var principal = Principal(BearerScheme, Guid.NewGuid());

        var context = await HandleAsync(principal, mtlsEnabled: true);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task BearerIdentityWithNodeId_SucceedsWhenMtlsDisabled()
    {
        var principal = Principal(BearerScheme, Guid.NewGuid());

        var context = await HandleAsync(principal, mtlsEnabled: false);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task IdentityWithoutNodeId_Fails()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString())], MtlsMiddleware.AuthenticationType));

        var context = await HandleAsync(principal, mtlsEnabled: true);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task MtlsIdentityWithMalformedNodeId_Fails()
    {
        var principal = Principal(MtlsMiddleware.AuthenticationType, nodeId: null, rawNodeId: "not-a-guid");

        var context = await HandleAsync(principal, mtlsEnabled: true);

        Assert.False(context.HasSucceeded);
    }

    private static ClaimsPrincipal Principal(string authenticationType, Guid? nodeId = null, string? rawNodeId = null)
    {
        var value = rawNodeId ?? nodeId?.ToString();
        var claims = new List<Claim>();
        if (value is not null)
        {
            claims.Add(new Claim("node_id", value));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    private static async Task<AuthorizationHandlerContext> HandleAsync(ClaimsPrincipal user, bool mtlsEnabled)
    {
        var requirement = new AgentIdentityRequirement();
        var handler = new AgentIdentityHandler(Options.Create(new MtlsOptions { Enabled = mtlsEnabled }));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await handler.HandleAsync(context);
        return context;
    }
}
