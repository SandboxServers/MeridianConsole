using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Contracts.Nodes;
using Microsoft.Win32;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class WindowsMachineActionsTests
{
    [SkippableFact]
    public void GetProcessImage_OwnProcess_ReturnsRunningWithItsImage()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var self = Process.GetCurrentProcess();

        var result = new WindowsMachineActions().GetProcessImage(self.Id, self.StartTime.ToUniversalTime());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsRunning);
        Assert.Equal(Environment.ProcessPath, result.Value.Path, ignoreCase: true);
    }

    [SkippableFact]
    public void GetProcessImage_ReusedPid_ReturnsNotRunning()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var self = Process.GetCurrentProcess();

        var result = new WindowsMachineActions().GetProcessImage(self.Id, self.StartTime.ToUniversalTime().AddHours(-1));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsRunning);
    }

    [SkippableFact]
    public void GetProcessImage_NoSuchPid_ReturnsNotRunning()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        // Windows process IDs are multiples of 4, so PID 3 never exists.
        var result = new WindowsMachineActions().GetProcessImage(3, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsRunning);
    }

    [SkippableFact]
    public void AddCertificate_NonRootStore_IsRefused()
    {
        // D-S240. The certificate keeps its private key on purpose: if the store guard is ever removed, the existing
        // private-key refusal still returns before any store is opened, so this test can never change the machine.
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=meridian-uat-addcert-test", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.True(certificate.HasPrivateKey);
        var actions = new WindowsMachineActions();

        foreach (var (location, store) in new[]
                 {
                     ("CurrentUser", "Root"),
                     ("LocalMachine", "TrustedPeople"),
                     ("LocalMachine", "AuthRoot"),
                     ("LocalMachine", "My"),
                     ("localmachine", "root"),
                 })
        {
            var result = actions.AddCertificate(location, store, certificate);

            Assert.True(result.IsFailure, $"{location}\\{store}");
            Assert.Contains("only LocalMachine\\Root is allowed", result.Error, StringComparison.Ordinal);
        }
    }

    private static (CngKey Key, X509Certificate2 Cert) AgentCertificateWithKey(string keyName, bool agentIdentity = true)
    {
        var key = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyCreationOptions = CngKeyCreationOptions.None,
        });

        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var issuerRequest = new CertificateRequest(
            new X500DistinguishedName($"CN={(agentIdentity ? AgentCertificateProfile.CaCommonName : "Some Other CA")}"),
            issuerKey, HashAlgorithmName.SHA384);
        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddDays(2));

        using var ecdsa = new ECDsaCng(key);
        var request = new CertificateRequest(new X500DistinguishedName("CN=uat-test-agent"), ecdsa, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri(AgentCertificateProfile.SpiffeId(Guid.NewGuid())));
        request.CertificateExtensions.Add(san.Build());
        using var issued = request.Create(issuer, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(8));
        return (key, issued.CopyWithPrivateKey(ecdsa));
    }

    private static bool KeyExists(string name) => CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider);

    private static void DeleteKeyIfExists(string name)
    {
        if (KeyExists(name))
        {
            using var key = CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            key.Delete();
        }
    }

    private static string NewKeyName() => $"meridian-uat-test-{Guid.NewGuid():N}";

    [SkippableFact]
    public void RemoveMatchedCertificate_AgentCertificateInMy_DeletesKeyThenRemoves()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var name = NewKeyName();
        try
        {
            var (key, cert) = AgentCertificateWithKey(name);
            using var keyHandle = key;
            using var certificate = cert;
            var removed = new List<X509Certificate2>();

            var result = WindowsMachineActions.RemoveMatchedCertificate(certificate, "My", false, removed.Add);

            Assert.True(result.IsSuccess, result.Error);
            Assert.False(KeyExists(name));
            Assert.Single(removed);
        }
        finally
        {
            DeleteKeyIfExists(name);
        }
    }

    [SkippableFact]
    public void RemoveMatchedCertificate_MachineKeyRequiredButUserKey_KeepsCertificateAndKey()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var name = NewKeyName();
        try
        {
            var (key, cert) = AgentCertificateWithKey(name);
            using var keyHandle = key;
            using var certificate = cert;
            var removed = new List<X509Certificate2>();

            var result = WindowsMachineActions.RemoveMatchedCertificate(certificate, "My", true, removed.Add);

            Assert.True(result.IsFailure);
            Assert.Contains("kept", result.Error, StringComparison.Ordinal);
            Assert.Contains("not a machine key", result.Error, StringComparison.Ordinal);
            Assert.True(KeyExists(name));
            Assert.Empty(removed);
        }
        finally
        {
            DeleteKeyIfExists(name);
        }
    }

    [SkippableFact]
    public void RemoveMatchedCertificate_AgentCertificateInRoot_KeepsKey()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var name = NewKeyName();
        try
        {
            var (key, cert) = AgentCertificateWithKey(name);
            using var keyHandle = key;
            using var certificate = cert;
            var removed = new List<X509Certificate2>();

            var result = WindowsMachineActions.RemoveMatchedCertificate(certificate, "Root", false, removed.Add);

            Assert.True(result.IsSuccess, result.Error);
            Assert.True(KeyExists(name));
            Assert.Single(removed);
        }
        finally
        {
            DeleteKeyIfExists(name);
        }
    }

    [SkippableFact]
    public void RemoveMatchedCertificate_ForeignCertificate_RefusedAndKeyKept()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var name = NewKeyName();
        try
        {
            var (key, cert) = AgentCertificateWithKey(name, agentIdentity: false);
            using var keyHandle = key;
            using var certificate = cert;
            var removed = new List<X509Certificate2>();

            var result = WindowsMachineActions.RemoveMatchedCertificate(certificate, "My", false, removed.Add);

            Assert.True(result.IsFailure);
            Assert.Contains("not an agent or UAT certificate", result.Error, StringComparison.Ordinal);
            Assert.True(KeyExists(name));
            Assert.Empty(removed);
        }
        finally
        {
            DeleteKeyIfExists(name);
        }
    }

    [SkippableFact]
    public void RemoveMatchedCertificate_KeyAlreadyDeleted_RemovesCertificate()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var name = NewKeyName();
        try
        {
            var (key, cert) = AgentCertificateWithKey(name);
            using var keyHandle = key;
            using var certificate = cert;
            using (var open = CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider))
            {
                open.Delete();
            }

            var removed = new List<X509Certificate2>();

            var result = WindowsMachineActions.RemoveMatchedCertificate(certificate, "My", false, removed.Add);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Single(removed);
        }
        finally
        {
            DeleteKeyIfExists(name);
        }
    }

    private static string NewRegistryPath() => $@"Software\MeridianUatTest-{Guid.NewGuid():N}";

    [SkippableFact]
    public void DeleteEmptySubKey_Empty_Deletes()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var path = NewRegistryPath();
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            hkcu.CreateSubKey(path).Dispose();

            var result = WindowsMachineActions.DeleteEmptySubKey(hkcu, path);

            Assert.True(result.IsSuccess, result.Error);
            using var after = hkcu.OpenSubKey(path);
            Assert.Null(after);
        }
        finally
        {
            hkcu.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [SkippableFact]
    public void DeleteEmptySubKey_HasValue_KeepsAndFails()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var path = NewRegistryPath();
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            using (var key = hkcu.CreateSubKey(path))
            {
                key.SetValue("x", "1");
            }

            var result = WindowsMachineActions.DeleteEmptySubKey(hkcu, path);

            Assert.True(result.IsFailure);
            Assert.Contains("not empty", result.Error, StringComparison.Ordinal);
            using var after = hkcu.OpenSubKey(path);
            Assert.NotNull(after);
            Assert.Equal("1", after.GetValue("x"));
        }
        finally
        {
            hkcu.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [SkippableFact]
    public void DeleteEmptySubKey_HasSubKey_KeepsAndFails()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var path = NewRegistryPath();
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            hkcu.CreateSubKey(path + @"\child").Dispose();

            var result = WindowsMachineActions.DeleteEmptySubKey(hkcu, path);

            Assert.True(result.IsFailure);
            Assert.Contains("not empty", result.Error, StringComparison.Ordinal);
            using var after = hkcu.OpenSubKey(path + @"\child");
            Assert.NotNull(after);
        }
        finally
        {
            hkcu.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [SkippableFact]
    public void DeleteEmptySubKey_Missing_Succeeds()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

        var result = WindowsMachineActions.DeleteEmptySubKey(hkcu, NewRegistryPath());

        Assert.True(result.IsSuccess, result.Error);
    }
}
