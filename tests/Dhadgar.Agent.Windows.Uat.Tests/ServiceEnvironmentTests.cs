using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Stack;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class ServiceEnvironmentTests
{
    private static readonly StackSettings Settings = new(15432, 5672, 15001, 15002, "pgpw", "mqpw", "capw");
    private static readonly RunPaths Paths = new(@"C:\repo", "20261027-120000-abcd");

    [Fact]
    public void NodesEnvironment_MatchesTheContractTable()
    {
        var env = ServiceHost.NodesEnvironment(Settings, Paths);

        Assert.Equal("Development", env["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("http://127.0.0.1:15001", env["ASPNETCORE_URLS"]);
        Assert.Equal(
            "Host=127.0.0.1;Port=15432;Database=dhadgar_nodes_uat;Username=postgres;Password=pgpw",
            env["ConnectionStrings__Postgres"]);
        Assert.Equal("127.0.0.1", env["RabbitMq__Host"]);
        Assert.Equal("uat", env["RabbitMq__Username"]);
        Assert.Equal("mqpw", env["RabbitMq__Password"]);
        Assert.Equal("/", env["RabbitMq__VirtualHost"]);
        Assert.Equal("true", env["Mtls__ForwardedCertificate__Enabled"]);
        Assert.Equal("127.0.0.1/32", env["Mtls__ForwardedCertificate__TrustedProxies__0"]);
        Assert.Equal("::1/128", env["Mtls__ForwardedCertificate__TrustedProxies__1"]);
        Assert.Equal("5", env["Mtls__RenewalGraceMinutes"]);
        Assert.Equal(Paths.Ca, env["Nodes__CaStoragePath"]);
        Assert.Equal("capw", env["Nodes__CaKeyPassword"]);
    }

    [Fact]
    public void GatewayEnvironment_MatchesTheContractTable_AndUsesDestinationD1()
    {
        var env = ServiceHost.GatewayEnvironment(Settings);

        Assert.Equal("https://localhost:15002", env["ASPNETCORE_URLS"]);
        Assert.Equal("AllowCertificate", env["AgentIngress__ClientCertificateMode"]);
        Assert.Equal("http://127.0.0.1:15001/", env["ReverseProxy__Clusters__nodes__Destinations__d1__Address"]);
    }

    [Fact]
    public void Environments_ContainNoAgentKeys()
    {
        var keys = ServiceHost.NodesEnvironment(Settings, Paths).Keys.Concat(ServiceHost.GatewayEnvironment(Settings).Keys);

        Assert.DoesNotContain(keys, k => k.StartsWith("Agent__", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StackImages_AreTheNodesAndGatewayBuilds()
    {
        Assert.EndsWith(@"Dhadgar.Nodes\bin\Release\net10.0\Dhadgar.Nodes.exe", Paths.StackImages[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"Dhadgar.Gateway\bin\Release\net10.0\Dhadgar.Gateway.exe", Paths.StackImages[1], StringComparison.OrdinalIgnoreCase);
    }
}
