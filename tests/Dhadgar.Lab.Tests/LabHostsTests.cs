using Dhadgar.Lab;

namespace Dhadgar.Lab.Tests;

public sealed class LabHostsTests
{
    [Theory]
    [InlineData("http://127.0.0.1:15040/", true)]
    [InlineData("http://127.0.0.1:5040/", false)]
    [InlineData("http://10.0.0.5:15040/", false)]
    [InlineData("file:///", false)]
    [InlineData("http://localhost.evil.com:15040/", false)]
    public void IsLabEndpoint_KnownCases_MatchesExpectation(string endpoint, bool expected)
    {
        Assert.Equal(expected, LabHosts.IsLabEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public void IsLabEndpoint_RelativeUri_ReturnsFalseWithoutThrowing()
    {
        Assert.False(LabHosts.IsLabEndpoint(new Uri("/x", UriKind.Relative)));
    }

    [Fact]
    public void IsLabEndpoint_UserinfoTrick_Refused()
    {
        Assert.False(LabHosts.IsLabEndpoint(new Uri("http://127.0.0.1@evil.com:15040/")));
    }

    [Fact]
    public void IsLabInfraEndpoint_RelativeUri_ReturnsFalseWithoutThrowing()
    {
        Assert.False(LabHosts.IsLabInfraEndpoint(new Uri("/x", UriKind.Relative)));
    }

    [Fact]
    public void IsLabInfraEndpoint_RabbitMqManagementOnLoopback_Accepted()
    {
        Assert.True(LabHosts.IsLabInfraEndpoint(new Uri($"http://127.0.0.1:{LabPorts.RabbitMqManagement}/")));
    }

    [Fact]
    public void IsLabInfraEndpoint_ServicePort_Refused()
    {
        Assert.False(LabHosts.IsLabInfraEndpoint(new Uri($"http://127.0.0.1:{LabPorts.Nodes}/")));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8789/status", true)]
    [InlineData("http://localhost:8789/mcp", true)]
    [InlineData("http://[::1]:8789/status", true)]
    [InlineData("https://127.0.0.1:8789/status", false)]
    [InlineData("http://127.0.0.1:15040/", false)]
    [InlineData("http://127.0.0.1:8790/status", false)]
    [InlineData("http://127.0.0.1:5040/", false)]
    [InlineData("http://10.0.0.5:8789/status", false)]
    [InlineData("http://localhost.evil.com:8789/status", false)]
    [InlineData("http://127.0.0.1@evil.com:8789/status", false)]
    public void IsDaemonEndpoint_KnownCases_MatchesExpectation(string endpoint, bool expected)
    {
        Assert.Equal(expected, LabHosts.IsDaemonEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public void IsDaemonEndpoint_DaemonPort_IsLabDefaultsDaemonPort()
    {
        Assert.True(LabHosts.IsDaemonEndpoint(new Uri($"http://127.0.0.1:{LabDefaults.DaemonPort}/status")));
    }

    [Fact]
    public void IsDaemonEndpoint_RelativeUri_ReturnsFalseWithoutThrowing()
    {
        Assert.False(LabHosts.IsDaemonEndpoint(new Uri("/status", UriKind.Relative)));
    }

    [Fact]
    public void IsLabEndpoint_DaemonPort_StillRefused()
    {
        // wave 5: the daemon port is not a service port. Widening ServicePorts is not the fix (the HTTP probes gate on it).
        Assert.False(LabHosts.IsLabEndpoint(new Uri($"http://127.0.0.1:{LabDefaults.DaemonPort}/status")));
    }
}
