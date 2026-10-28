using Dhadgar.Nodes.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Tests.Auth;

public sealed class NodesAuthenticationSchemesTests
{
    [Theory]
    [InlineData("/agents/00000000-0000-0000-0000-000000000001/heartbeat")]
    [InlineData("/agents/ca-certificate")]
    [InlineData("/AGENTS/x")]
    [InlineData("/hubs/agent/negotiate")]
    public void AgentPaths_SelectAgentMtls(string path)
    {
        Assert.Equal(AgentMtlsAuthenticationHandler.SchemeName, Select(path));
    }

    [Theory]
    [InlineData("/organizations/00000000-0000-0000-0000-000000000001/nodes")]
    [InlineData("/agentsx")]
    [InlineData("/hubs/agentx")]
    [InlineData("/hubs/console")]
    public void OtherPaths_SelectJwtBearer(string path)
    {
        Assert.Equal("Bearer", Select(path));
    }

    private static string Select(string path)
    {
        var services = new ServiceCollection();
        services.Configure<MtlsOptions>(_ => { });
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;

        return NodesAuthenticationSchemes.SelectScheme(context);
    }
}
