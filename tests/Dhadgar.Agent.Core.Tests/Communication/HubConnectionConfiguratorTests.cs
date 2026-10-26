using Xunit;
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
