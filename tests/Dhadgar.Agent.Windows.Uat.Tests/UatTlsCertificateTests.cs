using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Service;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>The run-scoped TLS leaf (F14): profile, manifest order, the public-only add and the Redactor.</summary>
public class UatTlsCertificateTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-tls-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly Redactor _redactor = new();
    private readonly RecordingActions _actions = new();

    public UatTlsCertificateTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        Directory.CreateDirectory(_paths.RunDir);
        _manifest = new CleanupManifest(_paths.Manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [SkippableFact]
    public void Create_Leaf_HasServerAuthProfileAndNoPrivateKeyInRoot()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var result = UatTlsCertificate.Create(_paths, "run-1", _manifest, _redactor, _actions);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_actions.Added);
        Assert.Equal("LocalMachine", added.StoreLocation);
        Assert.Equal("Root", added.StoreName);
        Assert.False(added.PrivateKeyAtAdd);
        Assert.False(added.Certificate.HasPrivateKey);
        Assert.Equal(result.Value.ThumbprintSha1, added.Certificate.Thumbprint);
        Assert.StartsWith("CN=localhost", added.Certificate.Subject, StringComparison.Ordinal);
        Assert.Contains("O=Meridian Console UAT run-1", added.Certificate.Subject, StringComparison.Ordinal);

        var basic = added.Certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        Assert.False(basic.CertificateAuthority);
        Assert.True(basic.Critical);

        var eku = added.Certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Equal(["1.3.6.1.5.5.7.3.1"], eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Select(o => o.Value));

        var san = added.Certificate.Extensions.Single(e => e.Oid?.Value == "2.5.29.17");
        var sanText = new System.Security.Cryptography.AsnEncodedData(san.Oid, san.RawData).Format(false);
        Assert.Contains("localhost", sanText, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", sanText, StringComparison.Ordinal);

        Assert.True((added.Certificate.NotAfter - added.Certificate.NotBefore) <= TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5));
        Assert.False(AgentCertificateMatcher.IsAgentCa(added.Certificate));
    }

    [SkippableFact]
    public void Create_ManifestEntries_PrecedeTheWriteAndTheAdd()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var result = UatTlsCertificate.Create(_paths, "run-1", _manifest, _redactor, _actions);

        Assert.True(result.IsSuccess);
        var entries = CleanupManifest.Load(_paths.Manifest).Value;
        Assert.Equal(3, entries.Count);
        Assert.IsType<DirectoryEntry>(entries[0]);
        Assert.IsType<FileEntry>(entries[1]);
        var certificate = Assert.IsType<CertificateEntry>(entries[2]);
        Assert.Equal(result.Value.ThumbprintSha1, certificate.ThumbprintSha1);
        Assert.True(File.Exists(result.Value.PfxPath));
    }

    [SkippableFact]
    public void Create_Password_IsRedactedAndNotInToString()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var result = UatTlsCertificate.Create(_paths, "run-1", _manifest, _redactor, _actions);

        Assert.True(result.IsSuccess);
        var password = result.Value.Password;
        Assert.DoesNotContain(password, _redactor.Redact(password), StringComparison.Ordinal);
        Assert.DoesNotContain(password, result.Value.ToString(), StringComparison.Ordinal);
    }
}
