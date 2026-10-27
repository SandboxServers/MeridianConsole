using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Communication;

public class ControlPlaneClientTests
{
    [Fact]
    public async Task ConnectAsync_HttpEndpoint_ReturnsFailure_NoThrow()
    {
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateClient("http://localhost", new StubCertificateStore(), meter, activity);

        var result = await client.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[ControlPlane.InsecureEndpoint]", result.Error);
        Assert.Equal(ConnectionState.Failed, client.State);
    }

    [Fact]
    public async Task ConnectAsync_NoCertificate_ReturnsFailure()
    {
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateClient("https://localhost", new StubCertificateStore(), meter, activity);

        var result = await client.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[ControlPlane.NoCertificate]", result.Error);
        Assert.Equal(ConnectionState.Failed, client.State);
    }

    [Fact]
    public async Task ConnectAsync_CertificateStoreThrows_ReturnsFailure_NoThrow()
    {
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateClient("https://localhost", new ThrowingCertificateStore(), meter, activity);

        var result = await client.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[ControlPlane.ConnectFailed]", result.Error);
        Assert.Contains("CryptographicException", result.Error);
        Assert.DoesNotContain("store fault", result.Error);
        Assert.Equal(ConnectionState.Failed, client.State);
    }

    [Fact]
    public async Task ConnectAsync_InvalidEndpoint_ReturnsFailure_NoThrow()
    {
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateClient("not a uri", new StubCertificateStore(), meter, activity);

        var result = await client.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[ControlPlane.InvalidEndpoint]", result.Error);
        Assert.Equal(ConnectionState.Failed, client.State);
    }

    [Fact]
    public async Task ConnectAsync_CallerCancelledDuringConnect_Throws()
    {
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        using var cts = new CancellationTokenSource();
        await using var client = CreateClient("https://localhost", new CancellingCertificateStore(cts), meter, activity);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));

        Assert.Equal(ConnectionState.Failed, client.State);
    }

    private static ControlPlaneClient CreateClient(
        string endpoint,
        ICertificateStore store,
        AgentMeter meter,
        AgentActivitySource activity)
    {
        return new ControlPlaneClient(
            Options.Create(new AgentOptions
            {
                NodeId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                ControlPlane = { Endpoint = endpoint }
            }),
            store,
            meter,
            activity,
            NullLogger<ControlPlaneClient>.Instance);
    }

    private sealed class ThrowingCertificateStore : ICertificateStore
    {
        public X509Certificate2? GetClientCertificate() => throw new CryptographicException("store fault");

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

    private sealed class CancellingCertificateStore(CancellationTokenSource cts) : ICertificateStore
    {
        public X509Certificate2? GetClientCertificate()
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
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

    /// <summary>
    /// Certificate store with no enrolled certificate.
    /// </summary>
    private sealed class StubCertificateStore : ICertificateStore
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
