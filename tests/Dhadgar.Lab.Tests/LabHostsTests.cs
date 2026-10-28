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
    public void IsLabEndpoint_KnownCases_MatchesExpectation(string uri, bool expected)
    {
        Assert.Equal(expected, LabHosts.IsLabEndpoint(new Uri(uri)));
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
}
