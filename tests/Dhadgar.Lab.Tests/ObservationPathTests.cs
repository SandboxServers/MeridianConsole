using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests;

/// <summary>
/// The observation path (R-OBSERVE, lab mutant LM8): what the lab reads to observe a service goes direct to its host port,
/// never through a Toxiproxy proxy, so a fault cannot hide what the lab is looking at.
/// </summary>
public sealed class ObservationPathTests
{
    [Fact]
    public void Reader_ResolvesToDirectPostgresPort_AsLabReader()
    {
        using var vault = new SecretVault();
        var refs = new LabCredentialRefs();
        refs.Set(LabCredentialRefs.LabReader, vault.Add(SecretKind.Password, "observation-path-test"));
        var connections = new LabConnectionStrings(vault, refs);

        Result<string> reader = connections.Reader("nodes");

        Assert.True(reader.IsSuccess, reader.Error);
        string connection = reader.Value!;
        Assert.Contains($"Port={LabPorts.Postgres};", connection, StringComparison.Ordinal);
        Assert.Contains("Username=lab_reader;", connection, StringComparison.Ordinal);
        Assert.DoesNotContain("meridian-lab-toxiproxy", connection, StringComparison.Ordinal);
        foreach (ProxyDefinition proxy in ProxyCatalog.All)
        {
            Assert.DoesNotContain($"Port={proxy.ListenPort};", connection, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HttpDirect_ToNodes_ResolvesToNodesDirectPort()
    {
        Result<Uri> uri = LabHttp.Compose("nodes", "/health");

        Assert.True(uri.IsSuccess, uri.Error);
        Assert.Equal(LabPorts.Nodes, uri.Value!.Port);
    }

    [Fact]
    public void HttpDirect_ToSink_ResolvesToLoopbackSinkPort()
    {
        Result<Uri> uri = LabHttp.Compose("sink", "/_sink/requests");

        Assert.True(uri.IsSuccess, uri.Error);
        Assert.Equal(LabPorts.Sink, uri.Value!.Port);
        Assert.Equal("127.0.0.1", uri.Value!.Host);
    }

    [Fact]
    public void HttpDirect_ToSink_SchemeRelativePath_IsRefused()
    {
        Result<Uri> uri = LabHttp.Compose("sink", "//evil.example/x");

        Assert.True(uri.IsFailure);
        Assert.True(
            uri.Error.StartsWith("[Lab.Untrusted]", StringComparison.Ordinal) || uri.Error.StartsWith("[Lab.InvalidArgument]", StringComparison.Ordinal),
            uri.Error);
    }
}
