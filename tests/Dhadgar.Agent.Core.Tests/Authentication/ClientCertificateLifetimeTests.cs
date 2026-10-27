using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

/// <summary>
/// Certificate lifetime: a certificate handed out before <see cref="IClientCertificateProvider.Invalidate"/>
/// must stay usable until the next generation, and the mTLS handler must pick the certificate per handshake.
/// </summary>
public sealed class ClientCertificateLifetimeTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("6f1c2b3a-4d5e-4f60-8a71-92b3c4d5e6f7");

    private readonly TestAgentCa _ca = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    private readonly ECDsa _otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);

    public void Dispose()
    {
        _otherKey.Dispose();
        _key.Dispose();
        _ca.Dispose();
    }

    [Fact]
    public void HeldCertificate_SurvivesInvalidate_AndIsDisposedOnTheNextGeneration()
    {
        var store = new PfxCertificateStore(IssueWithKey(_key));
        using var provider = new ClientCertificateProvider(store);

        var held = provider.Current!;
        provider.Invalidate();

        // The instance a handshake already holds still signs after Invalidate
        using (var heldKey = held.GetECDsaPrivateKey())
        {
            Assert.NotNull(heldKey);
            var signature = heldKey!.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA384);
            Assert.NotEmpty(signature);
        }

        // The next Invalidate retires the next generation, which disposes the held one
        store.Pfx = IssueWithKey(_otherKey).Export(X509ContentType.Pfx);
        var next = provider.Current!;
        Assert.NotSame(held, next);
        provider.Invalidate();
        // Disposed: the native handle is gone, so the private key can no longer be reached
        Assert.ThrowsAny<CryptographicException>(() => held.GetECDsaPrivateKey());
        next.Dispose();
    }

    [Fact]
    public void Dispose_DisposesTheRetiredCertificate()
    {
        var store = new PfxCertificateStore(IssueWithKey(_key));
        var provider = new ClientCertificateProvider(store);

        var held = provider.Current!;
        provider.Invalidate();
        provider.Dispose();

        Assert.ThrowsAny<CryptographicException>(() => held.GetECDsaPrivateKey());
    }

    [Fact]
    public void ControlPlaneMtlsHandler_SelectsTheProviderCertificateOnEachHandshake()
    {
        var provider = new StubClientCertificateProvider(IssueWithKey(_key));
        var services = new ServiceCollection();
        services.AddSingleton<IClientCertificateProvider>(provider);
        services.AddAgentHttpClients();
        using var sp = services.BuildServiceProvider();

        HttpMessageHandler handler = sp.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("ControlPlaneMtls");
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        var socketsHandler = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(socketsHandler.AllowAutoRedirect);
        Assert.Equal(TimeSpan.FromMinutes(1), socketsHandler.PooledConnectionLifetime);

        var select = socketsHandler.SslOptions.LocalCertificateSelectionCallback!;
        Assert.NotNull(select);

        var first = provider.Current!;
        Assert.Same(first, select(null!, "h", null!, null!, null!));

        var second = IssueWithKey(_otherKey);
        provider.Current = second;
        Assert.Same(second, select(null!, "h", null!, null!, null!));

        provider.Current = null;
        Assert.Null(select(null!, "h", null!, null!, null!));

        first.Dispose();
        second.Dispose();
    }

    /// <summary>Issues a leaf for <paramref name="key"/> with its private key attached.</summary>
    private X509Certificate2 IssueWithKey(ECDsa key)
    {
        using var leaf = _ca.Issue(key.ExportSubjectPublicKeyInfo(), AgentCertificateProfile.SpiffeId(NodeId));
        return leaf.CopyWithPrivateKey(key);
    }

    /// <summary>Serves a PFX copy of the certificate, so each read returns a fresh instance with its key.</summary>
    private sealed class PfxCertificateStore(X509Certificate2 certificate) : ICertificateStore
    {
        public byte[]? Pfx { get; set; } = certificate.Export(X509ContentType.Pfx);

        public X509Certificate2? GetClientCertificate() =>
            Pfx is null ? null : X509CertificateLoader.LoadPkcs12(Pfx, null);

        public Task StoreCertificateAsync(X509Certificate2 cert, byte[] privateKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveCertificateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public X509Certificate2? GetCaCertificate() => null;

        public Task StoreCaCertificateAsync(X509Certificate2 cert, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public bool NeedsRenewal(int thresholdDays) => false;
    }

    /// <summary>A provider whose <see cref="Current"/> the test sets directly.</summary>
    private sealed class StubClientCertificateProvider(X509Certificate2? current) : IClientCertificateProvider
    {
        public X509Certificate2? Current { get; set; } = current;

        public void Invalidate()
        {
        }
    }
}
