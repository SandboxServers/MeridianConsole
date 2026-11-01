using System.Globalization;
using Dhadgar.Lab;
using Dhadgar.Lab.SelfCheck;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The self-check recovery daemon URIs (wave 5 fix G): <c>GetStatusAsync</c> and <c>CallToolAsync</c> reach the child daemon, so
/// their URIs come from <see cref="SelfCheckRecovery.DaemonUri"/>, which accepts the daemon port through
/// <see cref="LabHosts.IsDaemonEndpoint"/>, not <see cref="LabHosts.IsLabEndpoint"/> (the daemon port is not a service port).
/// Pure, no sockets, no processes, no Docker.
/// </summary>
public sealed class SelfCheckRecoveryGuardTests
{
    [Fact]
    public void ToolCallUrl_IsTheDaemonMcpEndpoint()
    {
        Result<Uri> target = SelfCheckRecovery.DaemonUri(LabDefaults.McpPath);

        Assert.True(target.IsSuccess, target.Error);
        Assert.Equal("http://127.0.0.1:8789/mcp", target.Value!.ToString());
        Assert.True(LabHosts.IsDaemonEndpoint(target.Value!));
        Assert.False(LabHosts.IsLabEndpoint(target.Value!));
    }

    [Fact]
    public void StatusUrl_IsTheDaemonStatusEndpoint()
    {
        Result<Uri> target = SelfCheckRecovery.DaemonUri(LabDefaults.StatusPath);

        Assert.True(target.IsSuccess, target.Error);
        Assert.Equal(
            "http://127.0.0.1:" + LabDefaults.DaemonPort.ToString(CultureInfo.InvariantCulture) + LabDefaults.StatusPath,
            target.Value!.ToString());
        Assert.True(LabHosts.IsDaemonEndpoint(target.Value!));
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
