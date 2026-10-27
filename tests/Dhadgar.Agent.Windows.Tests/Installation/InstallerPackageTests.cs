using System.IO;
using System.Linq;
using System.Xml.Linq;
using Dhadgar.Agent.Windows.Installation;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Installation;

/// <summary>
/// Installer-to-agent seam and MSI baseline guards (D-S230, D-S236). Plain facts that read the WiX sources, so they
/// run on Linux CI, where the MSI is never built.
/// </summary>
public sealed class InstallerPackageTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace Util = "http://wixtoolset.org/schemas/v4/wxs/util";

    private const string AgentFolder = "src/Agents/Dhadgar.Agent.Windows";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dhadgar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Dhadgar.sln not found above the test output folder.");
    }

    private static XDocument LoadPackage() =>
        XDocument.Load(Path.Combine(RepoRoot(), AgentFolder, "Installer", "Package.wxs"));

    private static XDocument LoadWixproj() =>
        XDocument.Load(Path.Combine(RepoRoot(), AgentFolder, "Installer", "Dhadgar.Agent.Windows.Installer.wixproj"));

    private static XDocument LoadAgentCsproj() =>
        XDocument.Load(Path.Combine(RepoRoot(), AgentFolder, "Dhadgar.Agent.Windows.csproj"));

    private static XElement Property(XDocument doc, string id) =>
        doc.Descendants(Wix + "Property").Single(p => (string?)p.Attribute("Id") == id);

    [Fact]
    public void PackageWxs_HklmRegistryKeys_UseAgentRegistryKeyPath()
    {
        var doc = LoadPackage();
        var keys = doc.Descendants()
            .Where(e => (e.Name == Wix + "RegistryKey" || e.Name == Wix + "RegistrySearch")
                        && (string?)e.Attribute("Root") == "HKLM")
            .ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.Equal(AgentRegistry.KeyPath, (string?)k.Attribute("Key")));
    }

    [Fact]
    public void PackageWxs_TokenValue_UsesEnrollmentTokenValueName()
    {
        var doc = LoadPackage();
        var value = doc.Descendants(Wix + "RegistryValue")
            .Single(v => (string?)v.Attribute("Value") == "[ENROLLMENT_TOKEN]");

        Assert.Equal(AgentRegistry.EnrollmentTokenValueName, (string?)value.Attribute("Name"));
    }

    [Fact]
    public void PackageWxs_EndpointValue_UsesControlPlaneEndpointValueName()
    {
        var doc = LoadPackage();
        var value = doc.Descendants(Wix + "RegistryValue")
            .Single(v => (string?)v.Attribute("Value") == "[CONTROL_PLANE_ENDPOINT]");
        var search = doc.Descendants(Wix + "RegistrySearch")
            .Single(s => (string?)s.Attribute("Id") == "ControlPlaneEndpointSearch");

        Assert.Equal(AgentRegistry.ControlPlaneEndpointValueName, (string?)value.Attribute("Name"));
        Assert.Equal(AgentRegistry.ControlPlaneEndpointValueName, (string?)search.Attribute("Name"));
    }

    [Fact]
    public void PackageWxs_TokenProperty_IsSecureAndHidden()
    {
        var token = Property(LoadPackage(), "ENROLLMENT_TOKEN");

        Assert.Equal("yes", (string?)token.Attribute("Secure"));
        Assert.Equal("yes", (string?)token.Attribute("Hidden"));
    }

    [Fact]
    public void PackageWxs_EndpointProperties_AreSecure()
    {
        var doc = LoadPackage();

        Assert.Equal("yes", (string?)Property(doc, "CONTROL_PLANE_ENDPOINT").Attribute("Secure"));
        Assert.Equal("yes", (string?)Property(doc, "CMDLINE_CONTROL_PLANE_ENDPOINT").Attribute("Secure"));
    }

    [Fact]
    public void PackageWxs_LaunchConditions_UseWindowsBuildNotVersionNt()
    {
        var conditions = LoadPackage().Descendants(Wix + "Launch")
            .Select(l => (string?)l.Attribute("Condition") ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(conditions, c => c.Contains("VersionNT >=", System.StringComparison.Ordinal));
        Assert.Contains(conditions, c => c.Contains("WindowsBuild >= 10240", System.StringComparison.Ordinal));
    }

    [Fact]
    public void PackageWxs_ServiceElements_UseProgramServiceName()
    {
        var doc = LoadPackage();

        Assert.Equal(Program.ServiceName, (string?)doc.Descendants(Wix + "ServiceInstall").Single().Attribute("Name"));
        Assert.Equal(Program.ServiceName, (string?)doc.Descendants(Wix + "ServiceControl").Single().Attribute("Name"));
    }

    [Fact]
    public void PackageWxs_EventSource_UsesProgramEventLogSourceName()
    {
        var source = LoadPackage().Descendants(Util + "EventSource").Single();

        Assert.Equal(Program.EventLogSourceName, (string?)source.Attribute("Name"));
    }

    [Fact]
    public void PackageWxs_TokenKey_HasProtectedSddl()
    {
        var doc = LoadPackage();
        var tokenKey = doc.Descendants(Wix + "RegistryKey")
            .Single(k => k.Elements(Wix + "RegistryValue")
                .Any(v => (string?)v.Attribute("Value") == "[ENROLLMENT_TOKEN]"));
        var sddl = (string?)tokenKey.Elements(Wix + "PermissionEx").Single().Attribute("Sddl") ?? string.Empty;

        Assert.StartsWith("D:P", sddl, System.StringComparison.Ordinal);
        Assert.Contains(";;;SY)", sddl, System.StringComparison.Ordinal);
        Assert.Contains(";;;BA)", sddl, System.StringComparison.Ordinal);
    }

    [Fact]
    public void PackageWxs_UsesOnlySddlPermissions()
    {
        var doc = LoadPackage();

        Assert.Empty(doc.Descendants(Wix + "Permission"));
        Assert.Empty(doc.Descendants(Util + "PermissionEx"));
    }

    [Fact]
    public void Wixproj_InstallerPlatform_IsX64()
    {
        var platform = LoadWixproj().Descendants("InstallerPlatform").Single();

        Assert.Equal("x64", platform.Value);
    }

    [Fact]
    public void AgentCsproj_PublishesAppSettings()
    {
        var item = LoadAgentCsproj().Descendants("None")
            .SingleOrDefault(n => (string?)n.Attribute("Update") == "appsettings.json");

        Assert.NotNull(item);
        Assert.False(string.IsNullOrEmpty((string?)item.Attribute("CopyToPublishDirectory")));
        Assert.Equal("true", (string?)item.Attribute("ExcludeFromSingleFile"));
    }
}
