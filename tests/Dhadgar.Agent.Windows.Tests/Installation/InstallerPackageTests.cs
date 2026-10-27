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
        // D-S242: the one read of an OS key; pinned by PackageWxs_WindowsBuildNumber_IsReadFromCurrentVersionInThe64BitView.
        var keys = doc.Descendants()
            .Where(e => (e.Name == Wix + "RegistryKey" || e.Name == Wix + "RegistrySearch")
                        && (string?)e.Attribute("Root") == "HKLM"
                        && (string?)e.Attribute("Id") != "WindowsBuildNumberSearch")
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
    public void PackageWxs_WindowsVersionCondition_UsesRegistryBuildNumber()
    {
        var conditions = LoadPackage().Descendants(Wix + "Launch")
            .Select(l => (string?)l.Attribute("Condition") ?? string.Empty)
            .ToList();

        // D-S242: msiexec reports WindowsBuild 9600 and VersionNT 603 on Windows 10 and 11.
        Assert.DoesNotContain(conditions, c => System.Text.RegularExpressions.Regex.IsMatch(c, @"\bWindowsBuild\b"));
        Assert.DoesNotContain(conditions, c => c.Contains("VersionNT >=", System.StringComparison.Ordinal));
        Assert.Contains("Installed OR WINDOWS_BUILD_NUMBER >= 10240", conditions);
    }

    [Fact]
    public void PackageWxs_WindowsBuildNumber_IsReadFromCurrentVersionInThe64BitView()
    {
        var property = Property(LoadPackage(), "WINDOWS_BUILD_NUMBER");
        var search = Assert.Single(property.Elements(Wix + "RegistrySearch"));

        Assert.Equal("yes", (string?)property.Attribute("Secure"));
        Assert.Equal("WindowsBuildNumberSearch", (string?)search.Attribute("Id"));
        Assert.Equal("HKLM", (string?)search.Attribute("Root"));
        Assert.Equal(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", (string?)search.Attribute("Key"));
        Assert.Equal("CurrentBuildNumber", (string?)search.Attribute("Name"));
        Assert.Equal("raw", (string?)search.Attribute("Type"));
        Assert.Equal("always64", (string?)search.Attribute("Bitness"));
    }

    [Fact]
    public void PackageWxs_ServiceElements_UseProgramServiceName()
    {
        var doc = LoadPackage();

        Assert.Equal(Program.ServiceName, (string?)doc.Descendants(Wix + "ServiceInstall").Single().Attribute("Name"));
        Assert.Equal(Program.ServiceName, (string?)doc.Descendants(Wix + "ServiceControl").Single().Attribute("Name"));
    }

    [Fact]
    public void PackageWxs_ServiceInstall_IsInTheComponentWhoseKeyPathIsTheAgentExe()
    {
        var doc = LoadPackage();
        var exeName = LoadAgentCsproj().Descendants("AssemblyName").Single().Value + ".exe";
        var install = doc.Descendants(Wix + "ServiceInstall").Single();
        var component = install.Parent;

        // D-S244: Windows Installer installs a ServiceInstall row only from a component keyed by the service exe.
        Assert.NotNull(component);
        Assert.Equal(Wix + "Component", component.Name);
        Assert.Null(component.Attribute("Condition"));
        var keyPath = Assert.Single(component.Descendants(), e => (string?)e.Attribute("KeyPath") == "yes");
        Assert.Equal(Wix + "File", keyPath.Name);
        Assert.Equal(exeName, (string?)keyPath.Attribute("Name"));
        Assert.Same(component, doc.Descendants(Wix + "ServiceControl").Single().Parent);
        Assert.Single(install.Elements(Util + "ServiceConfig"));
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
    public void PackageWxs_CreateFolders_HaveProtectedSddlWithOwnerRightsAce()
    {
        // Every CreateFolder sits under %ProgramData%\Meridian Console (the MeridianDataFolder tree).
        var folders = LoadPackage().Descendants(Wix + "CreateFolder").ToList();

        Assert.Equal(4, folders.Count);
        foreach (var folder in folders)
        {
            var sddl = (string?)folder.Elements(Wix + "PermissionEx").Single().Attribute("Sddl") ?? string.Empty;

            Assert.StartsWith("D:P", sddl, System.StringComparison.Ordinal);
            Assert.Contains("(A;OICI;;;;OW)", sddl, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PackageWxs_EndpointLaunchCondition_AppliesOutsideUninstall()
    {
        var condition = LoadPackage().Descendants(Wix + "Launch")
            .Select(l => (string?)l.Attribute("Condition") ?? string.Empty)
            .Single(c => c.Contains("CONTROL_PLANE_ENDPOINT ~<<", System.StringComparison.Ordinal));

        Assert.Contains("REMOVE=\"ALL\"", condition, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Installed", condition, System.StringComparison.Ordinal);
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
