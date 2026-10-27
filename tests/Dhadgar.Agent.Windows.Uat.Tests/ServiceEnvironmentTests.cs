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
        // Path.Combine keeps the test's separator the platform's, so the same suffix matches on Windows and Linux.
        Assert.EndsWith(Path.Combine("Dhadgar.Nodes", "bin", "Release", "net10.0", "Dhadgar.Nodes.exe"), Paths.StackImages[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Dhadgar.Gateway", "bin", "Release", "net10.0", "Dhadgar.Gateway.exe"), Paths.StackImages[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartedSpecs_RunTheStackImages_NotAnyOtherExe()
    {
        // Asserts the spec StartAsync actually starts, so a changed FileName or WorkingDirectory fails here (N5).
        var nodes = ServiceHost.NodesSpec(Settings, Paths);
        var gateway = ServiceHost.GatewaySpec(Settings, Paths);

        Assert.Equal(Paths.StackImages[0], nodes.FileName);
        Assert.Equal(Paths.StackImages[1], gateway.FileName);
        Assert.Equal(Path.GetDirectoryName(Paths.StackImages[0]), nodes.WorkingDirectory);
        Assert.Equal(Path.GetDirectoryName(Paths.StackImages[1]), gateway.WorkingDirectory);
        Assert.Equal(ServiceHost.NodesEnvironment(Settings, Paths), nodes.Environment);
        Assert.Equal(ServiceHost.GatewayEnvironment(Settings), gateway.Environment);
    }

    [Fact]
    public void GatewayEnvironment_InServiceMode_AddsTheRunScopedPfx_AndKeepsTheUrls()
    {
        var tls = new GatewayTls(@"C:\repo\runs\run-1\tls\gateway.pfx", "pfxpw");
        var env = ServiceHost.GatewayEnvironment(Settings with { Tls = tls });

        Assert.Equal(@"C:\repo\runs\run-1\tls\gateway.pfx", env["Kestrel__Certificates__Default__Path"]);
        Assert.Equal("pfxpw", env["Kestrel__Certificates__Default__Password"]);
        Assert.Equal("https://localhost:15002", env["ASPNETCORE_URLS"]);
        Assert.Equal("AllowCertificate", env["AgentIngress__ClientCertificateMode"]);
    }

    [Fact]
    public void GatewayEnvironment_WithoutTls_ContainsNoKestrelCertificateKeys()
    {
        // Process mode and -StackOnly pass no TLS settings, so the Gateway keeps the user's dev certificate.
        var env = ServiceHost.GatewayEnvironment(Settings);

        Assert.DoesNotContain(env.Keys, k => k.StartsWith("Kestrel__Certificates__", StringComparison.Ordinal));
        Assert.Null(Settings.Tls);
    }

    [Fact]
    public void NodesEnvironment_NeverCarriesTheGatewayCertificate()
    {
        var tls = new GatewayTls(@"C:\repo\runs\run-1\tls\gateway.pfx", "pfxpw");
        var env = ServiceHost.NodesEnvironment(Settings with { Tls = tls }, Paths);

        Assert.DoesNotContain(env.Keys, k => k.StartsWith("Kestrel__Certificates__", StringComparison.Ordinal));
    }
}
