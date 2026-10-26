using Xunit;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Tests.Communication;

public sealed class HubConnectionConfiguratorTests
{
    [Fact]
    public async Task ConnectAsync_RunsRegisteredConfigurator()
    {
        var configurator = new FlagConfigurator();
        using var meter = new AgentMeter();
        using var activitySource = new AgentActivitySource();
        var options = new AgentOptions
        {
            ControlPlane = new ControlPlaneOptions { Endpoint = "https://127.0.0.1:1" }
        };

        await using var client = new ControlPlaneClient(
            Options.Create(options),
            new NullCertificateStore(),
            meter,
            activitySource,
            NullLogger<ControlPlaneClient>.Instance,
            [configurator]);

        // Nothing listens on port 1, so the connection fails; the hook runs while the connection is built.
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());

        Assert.True(configurator.WasInvoked);
    }

    [Fact]
    public async Task ConnectAsync_RunsConfiguratorAfterAgentOptions()
    {
        var configurator = new RecordingConfigurator();
        using var meter = new AgentMeter();
        using var activitySource = new AgentActivitySource();
        var options = new AgentOptions
        {
            NodeId = Guid.NewGuid(),
            ControlPlane = new ControlPlaneOptions { Endpoint = "https://127.0.0.1:1" }
        };

        await using var client = new ControlPlaneClient(
            Options.Create(options),
            new SelfSignedCertificateStore(),
            meter,
            activitySource,
            NullLogger<ControlPlaneClient>.Instance,
            [configurator]);

        // Nothing listens on port 1, so the connection fails after the hook has run.
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());

        Assert.True(configurator.HeaderPresent);
        Assert.Equal(1, configurator.CertificateCount);
    }

    [Fact]
    public async Task ConnectAsync_ConfiguratorThatChangesEndpointToHttp_FailsClosed()
    {
        using var meter = new AgentMeter();
        using var activitySource = new AgentActivitySource();
        var options = new AgentOptions
        {
            NodeId = Guid.NewGuid(),
            ControlPlane = new ControlPlaneOptions { Endpoint = "https://127.0.0.1:1" }
        };

        await using var client = new ControlPlaneClient(
            Options.Create(options),
            new SelfSignedCertificateStore(),
            meter,
            activitySource,
            NullLogger<ControlPlaneClient>.Instance,
            [new ActionConfigurator(o => o.Url = new Uri("http://127.0.0.1:1/hubs/agent"))]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_ConfiguratorThatRemovesClientCertificate_FailsClosed()
    {
        using var meter = new AgentMeter();
        using var activitySource = new AgentActivitySource();
        var options = new AgentOptions
        {
            NodeId = Guid.NewGuid(),
            ControlPlane = new ControlPlaneOptions { Endpoint = "https://127.0.0.1:1" }
        };

        await using var client = new ControlPlaneClient(
            Options.Create(options),
            new SelfSignedCertificateStore(),
            meter,
            activitySource,
            NullLogger<ControlPlaneClient>.Instance,
            [new ActionConfigurator(o => o.ClientCertificates = new X509CertificateCollection())]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        Assert.Contains("client certificate", ex.Message);
    }

    private sealed class ActionConfigurator(Action<HttpConnectionOptions> action) : IHubConnectionConfigurator
    {
        public void Configure(HttpConnectionOptions options) => action(options);
    }

    private sealed class RecordingConfigurator : IHubConnectionConfigurator
    {
        public bool HeaderPresent { get; private set; }

        public int? CertificateCount { get; private set; }

        public void Configure(HttpConnectionOptions options)
        {
            HeaderPresent = options.Headers.ContainsKey("X-Node-Id");
            CertificateCount = options.ClientCertificates?.Count;
        }
    }

    /// <summary>Returns a fresh self-signed client certificate on each call, as a real store would load one.</summary>
    private sealed class SelfSignedCertificateStore : ICertificateStore
    {
        public X509Certificate2? GetClientCertificate()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=seam-test", key, HashAlgorithmName.SHA256);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadCertificate(cert.RawData);
        }

        public Task StoreCertificateAsync(
            X509Certificate2 certificate,
            byte[] privateKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveCertificateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public X509Certificate2? GetCaCertificate() => null;

        public Task StoreCaCertificateAsync(
            X509Certificate2 certificate,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool NeedsRenewal(int thresholdDays) => false;
    }

    private sealed class FlagConfigurator : IHubConnectionConfigurator
    {
        public bool WasInvoked { get; private set; }

        public void Configure(HttpConnectionOptions options)
        {
            WasInvoked = true;
        }
    }

    private sealed class NullCertificateStore : ICertificateStore
    {
        public X509Certificate2? GetClientCertificate() => null;

        public Task StoreCertificateAsync(
            X509Certificate2 certificate,
            byte[] privateKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveCertificateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public X509Certificate2? GetCaCertificate() => null;

        public Task StoreCaCertificateAsync(
            X509Certificate2 certificate,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool NeedsRenewal(int thresholdDays) => false;
    }
}
