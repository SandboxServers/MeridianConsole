using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.AspNetCore.Http.Connections;
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

    [Fact]
    public async Task DisconnectAsync_ThenConnect_RebuildsWithFreshCertificate()
    {
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        var hub = new FakeLongPollingHub();
        var store = new FreshCertificateStore();
        await using var client = CreateClient(
            "https://localhost",
            store,
            meter,
            activity,
            new ControlPlaneHubTransport
            {
                Transports = HttpTransportType.LongPolling,
                HttpMessageHandlerFactory = _ => hub,
            });

        var connected = await client.ConnectAsync();
        Assert.True(connected.IsSuccess, connected.Error);
        Assert.Equal(ConnectionState.Connected, client.State);

        await client.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, client.State);

        // The next connect is a real rebuild: a fresh certificate is read and a new negotiate is sent.
        var reconnected = await client.ConnectAsync();
        Assert.True(reconnected.IsSuccess, reconnected.Error);
        Assert.Equal(2, store.Calls);
        Assert.Equal(2, hub.Negotiates);
    }

    private static ControlPlaneClient CreateClient(
        string endpoint,
        ICertificateStore store,
        AgentMeter meter,
        AgentActivitySource activity,
        ControlPlaneHubTransport? transport = null)
    {
        var identity = new AgentIdentityProvider();
        identity.Set(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        return new ControlPlaneClient(
            Options.Create(new AgentOptions
            {
                ControlPlane = { Endpoint = endpoint }
            }),
            store,
            meter,
            activity,
            NullLogger<ControlPlaneClient>.Instance,
            identity,
            transport);
    }

    /// <summary>
    /// Fake SignalR long-polling server. Negotiates, completes the handshake on the first poll, holds later
    /// polls open, and can fail the DELETE that StopAsync sends.
    /// </summary>
    private sealed class FakeLongPollingHub : HttpMessageHandler
    {
        private TaskCompletionSource _handshakeSent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _polls;

        public int Negotiates { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path.EndsWith("/negotiate", StringComparison.Ordinal))
            {
                // Each negotiate starts a fresh connection: reset the poll count and the handshake signal.
                Negotiates++;
                Interlocked.Exchange(ref _polls, 0);
                _handshakeSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                const string body =
                    "{\"negotiateVersion\":0,\"connectionId\":\"c1\",\"availableTransports\":[{\"transport\":\"LongPolling\",\"transferFormats\":[\"Text\"]}]}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                // The handshake request. The first poll answers it, so the poll must not return before this.
                _handshakeSent.TrySetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Get)
            {
                var poll = Interlocked.Increment(ref _polls);
                if (poll == 1)
                {
                    // The client sends the handshake only after its first poll returns, so answer it empty.
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(string.Empty, Encoding.UTF8, "application/octet-stream"),
                    };
                }

                if (poll == 2)
                {
                    await _handshakeSent.Task.WaitAsync(cancellationToken);

                    // Handshake response: an empty JSON object terminated by the SignalR record separator.
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}\u001e", Encoding.UTF8, "application/octet-stream"),
                    };
                }

                // Hold the poll open until the client stops.
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    /// <summary>
    /// Certificate store that issues a fresh self-signed client certificate on every read and counts the reads.
    /// </summary>
    private sealed class FreshCertificateStore : ICertificateStore
    {
        public int Calls { get; private set; }

        public X509Certificate2? GetClientCertificate()
        {
            Calls++;
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=agent-test", key, HashAlgorithmName.SHA256);
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
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
