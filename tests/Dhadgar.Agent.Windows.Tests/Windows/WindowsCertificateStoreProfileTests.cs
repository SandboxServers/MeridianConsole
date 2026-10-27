using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Windows.Windows;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

/// <summary>
/// Store tests against real Nodes-shaped certificates, in the CurrentUser stores so they need no Administrator.
/// Each test removes only the certificates it added (matched by thumbprint) and their private keys.
/// Windows only (<see cref="SkippableFactAttribute"/> skips elsewhere).
/// </summary>
public sealed class WindowsCertificateStoreProfileTests : IDisposable
{
    private const StoreLocation Location = StoreLocation.CurrentUser;

    private readonly ILogger<WindowsCertificateStore> _logger = Substitute.For<ILogger<WindowsCertificateStore>>();
    private readonly List<string> _thumbprints = [];
    private readonly List<X509Certificate2> _disposables = [];
    private readonly ECDsa _caKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    private readonly X509Certificate2 _ca;

    public WindowsCertificateStoreProfileTests()
    {
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={AgentCertificateProfile.CaCommonName}"),
            _caKey,
            HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        _ca = X509CertificateLoader.LoadCertificate(selfSigned.RawData);
        _thumbprints.Add(_ca.Thumbprint);
    }

    public void Dispose()
    {
        RemoveTrackedCertificates();

        foreach (var certificate in _disposables)
        {
            certificate.Dispose();
        }

        _ca.Dispose();
        _caKey.Dispose();
    }

    [SkippableFact]
    public async Task StoreCertificate_GetClientCertificate_ReturnsCertificateWithUsablePrivateKey()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        var store = new WindowsCertificateStore(_logger, location: Location);
        var leaf = IssueLeaf(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30));

        await store.StoreCertificateAsync(leaf.Certificate, leaf.Pkcs8);

        using var stored = store.GetClientCertificate();
        Assert.NotNull(stored);
        Assert.True(stored.HasPrivateKey);
        Assert.Equal(leaf.Certificate.Thumbprint, stored.Thumbprint);

        // The key must sign, and the stored certificate's public key must verify it
        var data = "agent handshake"u8.ToArray();
        using var storedKey = stored.GetECDsaPrivateKey();
        Assert.NotNull(storedKey);
        var signature = storedKey.SignData(data, HashAlgorithmName.SHA256);
        using var publicKey = stored.GetECDsaPublicKey();
        Assert.NotNull(publicKey);
        Assert.True(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256));
    }

    [SkippableFact]
    public async Task StoreCertificate_AfterRenewal_LeavesOnlyNewCertificate()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        var store = new WindowsCertificateStore(_logger, location: Location);
        var first = IssueLeaf(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(10));
        var renewal = IssueLeaf(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));

        await store.StoreCertificateAsync(first.Certificate, first.Pkcs8);
        await store.StoreCertificateAsync(renewal.Certificate, renewal.Pkcs8);

        var agentThumbprints = ThumbprintsInStore(StoreName.My, Location, AgentCertificateMatcher.IsAgentClientIdentity);
        Assert.Equal(new[] { renewal.Certificate.Thumbprint }, agentThumbprints);

        using var current = store.GetClientCertificate();
        Assert.NotNull(current);
        Assert.Equal(renewal.Certificate.Thumbprint, current.Thumbprint);
    }

    [SkippableFact]
    public async Task StoreCertificate_AfterRenewal_DeletesOldPrivateKey()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        var store = new WindowsCertificateStore(_logger, location: Location);
        var first = IssueLeaf(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(10));
        var renewal = IssueLeaf(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));

        await store.StoreCertificateAsync(first.Certificate, first.Pkcs8);

        string oldKeyName;
        CngProvider oldProvider;
        using (var oldStored = store.GetClientCertificate())
        {
            Assert.NotNull(oldStored);
            using var oldKey = oldStored.GetECDsaPrivateKey();
            var oldCng = Assert.IsType<ECDsaCng>(oldKey);
            oldKeyName = oldCng.Key.KeyName;
            oldProvider = oldCng.Key.Provider;
        }

        Assert.True(CngKey.Exists(oldKeyName, oldProvider, CngKeyOpenOptions.UserKey));

        await store.StoreCertificateAsync(renewal.Certificate, renewal.Pkcs8);

        Assert.False(CngKey.Exists(oldKeyName, oldProvider, CngKeyOpenOptions.UserKey));
    }

    [SkippableFact]
    public async Task StoreCaCertificate_LandsInAgentStore_NotRoot()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        var store = new WindowsCertificateStore(_logger, location: Location);
        using var caForStore = X509CertificateLoader.LoadCertificate(_ca.RawData);

        await store.StoreCaCertificateAsync(caForStore);

        using var fromStore = store.GetCaCertificate();
        Assert.NotNull(fromStore);
        Assert.Equal(_ca.Thumbprint, fromStore.Thumbprint);

        var inRoot = ThumbprintsInStore(StoreName.Root, Location, c => string.Equals(c.Thumbprint, _ca.Thumbprint, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(inRoot);
    }

    [SkippableFact]
    public async Task StoreCertificate_IgnoresNonAgentCertificateInMy()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        // A certificate with a private key that is not an agent certificate
        using var unrelatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unrelatedRequest = new CertificateRequest("CN=unrelated-service", unrelatedKey, HashAlgorithmName.SHA256);
        using var unrelatedSelfSigned = unrelatedRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var unrelatedPfx = unrelatedSelfSigned.Export(X509ContentType.Pfx, "unused-test-password");
        using var unrelated = X509CertificateLoader.LoadPkcs12(
            unrelatedPfx,
            "unused-test-password",
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        _thumbprints.Add(unrelated.Thumbprint);

        using (var my = new X509Store(StoreName.My, Location))
        {
            my.Open(OpenFlags.ReadWrite);
            my.Add(unrelated);
        }

        var store = new WindowsCertificateStore(_logger, location: Location);
        using var found = store.GetClientCertificate();

        Assert.NotEqual(unrelated.Thumbprint, found?.Thumbprint);
    }

    private (X509Certificate2 Certificate, byte[] Pkcs8) IssueLeaf(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={Guid.NewGuid():D}, O={AgentCertificateProfile.Organization}"),
            leafKey,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri(AgentCertificateProfile.SpiffeId(Guid.NewGuid())));
        request.CertificateExtensions.Add(san.Build());

        var generator = X509SignatureGenerator.CreateForECDsa(_caKey);
        using var issued = request.Create(
            _ca.SubjectName,
            generator,
            notBefore,
            notAfter,
            RandomNumberGenerator.GetBytes(16));

        var certificate = X509CertificateLoader.LoadCertificate(issued.RawData);
        _disposables.Add(certificate);
        _thumbprints.Add(certificate.Thumbprint);

        // PKCS#8 is what the store receives (Core README: PKCS#8 on every platform)
        return (certificate, leafKey.ExportPkcs8PrivateKey());
    }

    /// <summary>
    /// Returns the thumbprints of the certificates in a store that match.
    /// </summary>
    private static List<string> ThumbprintsInStore(StoreName name, StoreLocation location, Func<X509Certificate2, bool> matches)
    {
        var found = new List<string>();
        using var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadOnly);
        foreach (var certificate in store.Certificates)
        {
            if (matches(certificate))
            {
                found.Add(certificate.Thumbprint);
            }

            certificate.Dispose();
        }

        return found;
    }

    /// <summary>
    /// Removes the certificates this test added from My, the agent CA store and Root (CurrentUser), with their keys.
    /// </summary>
    private void RemoveTrackedCertificates()
    {
        RemoveTrackedFrom(new X509Store(StoreName.My, Location));
        RemoveTrackedFrom(new X509Store(WindowsCertificateStore.CaStoreName, Location));
        RemoveTrackedFrom(new X509Store(StoreName.Root, Location));
    }

    private void RemoveTrackedFrom(X509Store store)
    {
        using (store)
        {
            try
            {
                store.Open(OpenFlags.ReadWrite);
            }
            catch (CryptographicException)
            {
                return;
            }

            foreach (var certificate in store.Certificates)
            {
                if (!_thumbprints.Contains(certificate.Thumbprint, StringComparer.OrdinalIgnoreCase))
                {
                    certificate.Dispose();
                    continue;
                }

                DeletePrivateKey(certificate);
                store.Remove(certificate);
                certificate.Dispose();
            }
        }
    }

    private static void DeletePrivateKey(X509Certificate2 certificate)
    {
        try
        {
            using var ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa is ECDsaCng ecdsaCng)
            {
                ecdsaCng.Key.Delete();
            }
        }
        catch (CryptographicException)
        {
            // Cleanup is best effort
        }
    }
}
