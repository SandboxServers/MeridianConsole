using System.Globalization;
using Dhadgar.Lab;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The self-check recovery tool-call guard (wave 5 fix G): <c>CallToolAsync</c> reaches the child daemon's <c>/mcp</c> endpoint, so
/// its URL must pass <see cref="LabHosts.IsDaemonEndpoint"/>, not <see cref="LabHosts.IsLabEndpoint"/> (the daemon port is not a
/// service port). The guard is private and builds its URL from <see cref="LabDefaults.DaemonPort"/> and <see cref="LabDefaults.McpPath"/>,
/// so these cases test the same predicate with the same URL shape. Pure, no sockets, no processes, no Docker.
/// </summary>
public sealed class SelfCheckRecoveryGuardTests
{
    [Fact]
    public void ToolCallUrl_IsTheDaemonMcpEndpoint()
    {
        string url = "http://127.0.0.1:" + LabDefaults.DaemonPort.ToString(CultureInfo.InvariantCulture) + LabDefaults.McpPath;

        Assert.Equal("http://127.0.0.1:8789/mcp", url);
        Assert.True(LabHosts.IsDaemonEndpoint(new Uri(url, UriKind.Absolute)));
        Assert.False(LabHosts.IsLabEndpoint(new Uri(url, UriKind.Absolute)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:15040/mcp")]
    [InlineData("http://127.0.0.1:8790/mcp")]
    [InlineData("http://127.0.0.1:80/mcp")]
    public void ToolCallUrl_OtherLoopbackPort_IsRefused(string endpoint)
    {
        Assert.False(LabHosts.IsDaemonEndpoint(new Uri(endpoint, UriKind.Absolute)));
    }

    [Theory]
    [InlineData("http://10.0.0.5:8789/mcp")]
    [InlineData("http://192.168.1.10:8789/mcp")]
    [InlineData("http://localhost.evil.com:8789/mcp")]
    [InlineData("http://127.0.0.1@evil.com:8789/mcp")]
    public void ToolCallUrl_NonLoopbackHost_IsRefused(string endpoint)
    {
        Assert.False(LabHosts.IsDaemonEndpoint(new Uri(endpoint, UriKind.Absolute)));
    }
}
