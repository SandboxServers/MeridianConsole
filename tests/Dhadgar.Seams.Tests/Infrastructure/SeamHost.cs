extern alias nodes;

using nodes::Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// The Gateway and the backends it routes to, in one process. The Gateway forwards to "nodes" through
/// <see cref="NodesSeamFactory.Server"/>; agents talk to the Gateway through <see cref="GatewayHandler"/>.
/// </summary>
public sealed class SeamHost : IDisposable
{
    public SeamHost()
    {
        Nodes = new NodesSeamFactory();

        // Start Nodes first so its TestServer exists before the Gateway forwards to it.
        _ = Nodes.Server;

        Gateway = new GatewaySeamFactory(new Dictionary<string, Func<HttpMessageHandler>>
        {
            ["nodes"] = () => Nodes.Server.CreateHandler()
        });
    }

    public NodesSeamFactory Nodes { get; }

    public GatewaySeamFactory Gateway { get; }

    /// <summary>Records which clusters the Gateway forwarded to.</summary>
    public SeamForwarderFactory Forwarder => Gateway.Forwarder;

    /// <summary>A new in-process handler for the Gateway. The caller owns and disposes it.</summary>
    public HttpMessageHandler GatewayHandler() => Gateway.Server.CreateHandler();

    /// <summary>Creates a real enrollment token in Nodes for <paramref name="orgId"/>.</summary>
    public async Task<string> CreateEnrollmentTokenAsync(Guid orgId)
    {
        using var scope = Nodes.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IEnrollmentTokenService>();
        var (_, plainToken) = await tokens.CreateTokenAsync(orgId, "seam-user", "seam");
        return plainToken;
    }

    /// <summary>A real agent whose only route to the control plane is the Gateway.</summary>
    public SeamAgent CreateAgent() => new(GatewayHandler);

    public void Dispose()
    {
        Gateway.Dispose();
        Nodes.Dispose();
    }
}
