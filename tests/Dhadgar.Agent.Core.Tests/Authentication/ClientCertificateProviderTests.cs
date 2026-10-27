using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

public sealed class ClientCertificateProviderTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");

    private readonly TestAgentCa _ca = new();
    private readonly List<X509Certificate2> _owned = [];

    public void Dispose()
    {
        foreach (var certificate in _owned)
        {
            certificate.Dispose();
        }

        _ca.Dispose();
    }

    [Fact]
    public async Task Invalidate_MakesTheNextCurrentComeFromTheStore()
    {
        var first = IssueFor();
        var second = IssueFor();
        var store = new RecordingCertificateStore(first);
        using var provider = new ClientCertificateProvider(store);

        Assert.Equal(first.Thumbprint, provider.Current!.Thumbprint);

        // Store swaps the certificate, but the cached handshake certificate is unchanged until invalidated
        await store.StoreCertificateAsync(second, [1, 2, 3]);
        Assert.Equal(first.Thumbprint, provider.Current!.Thumbprint);

        provider.Invalidate();

        Assert.Equal(second.Thumbprint, provider.Current!.Thumbprint);
    }

    [Fact]
    public void Current_ReturnsNull_WhenTheStoreHasNoCertificate()
    {
        using var provider = new ClientCertificateProvider(new RecordingCertificateStore());

        Assert.Null(provider.Current);
    }

    [Fact]
    public void Current_AfterDispose_Throws()
    {
        var provider = new ClientCertificateProvider(new RecordingCertificateStore(IssueFor()));
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = provider.Current;
        });
    }

    /// <summary>Issues a leaf certificate for a fresh P-384 key bound to the test node.</summary>
    private X509Certificate2 IssueFor()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var certificate = _ca.Issue(key.ExportSubjectPublicKeyInfo(), AgentCertificateProfile.SpiffeId(NodeId));
        _owned.Add(certificate);
        return certificate;
    }
}
