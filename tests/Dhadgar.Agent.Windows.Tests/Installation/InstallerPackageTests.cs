using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    private const string TokenInstallCondition = "ENROLLMENT_TOKEN AND NOT REMOVE~=\"ALL\"";

    private static XElement CustomAction(XDocument doc, string id) =>
        doc.Descendants(Wix + "CustomAction").Single(a => (string?)a.Attribute("Id") == id);

    private static XElement Custom(XDocument doc, string action) =>
        doc.Descendants(Wix + "Custom").Single(c => (string?)c.Attribute("Action") == action);

    [Fact]
    public void PackageWxs_EnrollmentToken_IsFormattedOnlyIntoTheHiddenSetter()
    {
        // F29/D-S245: Hidden="yes" does not mask a formatted Registry-table value; /l*v logged the token.
        var doc = LoadPackage();
        var use = Assert.Single(doc.Descendants().Attributes(),
            a => a.Value.Contains("[ENROLLMENT_TOKEN]", System.StringComparison.Ordinal));
        var setter = use.Parent!;

        Assert.Equal("Value", use.Name.LocalName);
        Assert.Equal(Wix + "CustomAction", setter.Name);
        Assert.Equal("SetWriteEnrollmentToken", (string?)setter.Attribute("Id"));
        Assert.Equal("WriteEnrollmentToken", (string?)setter.Attribute("Property"));
        Assert.Equal("yes", (string?)setter.Attribute("HideTarget"));
        Assert.DoesNotContain(doc.Descendants(Wix + "RegistryValue"),
            v => (string?)v.Attribute("Name") == AgentRegistry.EnrollmentTokenValueName);
    }

    [Fact]
    public void PackageWxs_TokenActions_AreHiddenDeferredAndRunAsSystem()
    {
        var doc = LoadPackage();
        var write = CustomAction(doc, "WriteEnrollmentToken");
        var rollback = CustomAction(doc, "RollbackEnrollmentToken");
        var delete = CustomAction(doc, "DeleteEnrollmentToken");

        Assert.Equal("AgentInstallerActions", (string?)write.Attribute("BinaryRef"));
        Assert.Equal("WriteEnrollmentToken", (string?)write.Attribute("DllEntry"));
        Assert.Equal("deferred", (string?)write.Attribute("Execute"));
        Assert.Equal("no", (string?)write.Attribute("Impersonate"));
        Assert.Equal("check", (string?)write.Attribute("Return"));
        Assert.Equal("yes", (string?)write.Attribute("HideTarget"));
        Assert.Equal("DeleteEnrollmentToken", (string?)rollback.Attribute("DllEntry"));
        Assert.Equal("rollback", (string?)rollback.Attribute("Execute"));
        Assert.Equal("no", (string?)rollback.Attribute("Impersonate"));
        Assert.Equal("DeleteEnrollmentToken", (string?)delete.Attribute("DllEntry"));
        Assert.Equal("deferred", (string?)delete.Attribute("Execute"));
        Assert.Equal("no", (string?)delete.Attribute("Impersonate"));
    }

    [Fact]
    public void PackageWxs_TokenActions_AreSequencedAroundTheRegistryWrite()
    {
        var doc = LoadPackage();

        Assert.Equal("RollbackEnrollmentToken", (string?)Custom(doc, "SetWriteEnrollmentToken").Attribute("Before"));
        Assert.Equal("WriteEnrollmentToken", (string?)Custom(doc, "RollbackEnrollmentToken").Attribute("Before"));
        Assert.Equal("WriteRegistryValues", (string?)Custom(doc, "WriteEnrollmentToken").Attribute("After"));
        Assert.Equal("RemoveRegistryValues", (string?)Custom(doc, "DeleteEnrollmentToken").Attribute("Before"));
        foreach (var id in new[] { "SetWriteEnrollmentToken", "RollbackEnrollmentToken", "WriteEnrollmentToken" })
        {
            Assert.Equal(TokenInstallCondition, (string?)Custom(doc, id).Attribute("Condition"));
        }

        Assert.Equal("REMOVE~=\"ALL\"", (string?)Custom(doc, "DeleteEnrollmentToken").Attribute("Condition"));
    }

    [Fact]
    public void PackageWxs_AgentKey_IsProtectedByAnUnconditionalComponent()
    {
        var doc = LoadPackage();
        var key = Assert.Single(doc.Descendants(Wix + "RegistryKey"), k => k.Elements(Wix + "PermissionEx").Any());
        var component = key.Parent;
        var sddl = (string?)key.Elements(Wix + "PermissionEx").Single().Attribute("Sddl");

        // D-S245: the ACL sat on the token component, so an install without a token left the key unprotected.
        Assert.Equal(AgentRegistry.KeyPath, (string?)key.Attribute("Key"));
        Assert.Equal("yes", (string?)key.Attribute("ForceCreateOnInstall"));
        Assert.Equal("D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)", sddl);
        Assert.NotNull(component);
        Assert.Equal("InstallPathComponent", (string?)component.Attribute("Id"));
        Assert.Null(component.Attribute("Condition"));
    }

    [Fact]
    public void Installer_BindsTheInstallerActionsSfxDll()
    {
        var binary = LoadPackage().Descendants(Wix + "Binary")
            .Single(b => (string?)b.Attribute("Id") == "AgentInstallerActions");
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "src", "Agents", "Dhadgar.Agent.Windows.InstallerActions",
            "Dhadgar.Agent.Windows.InstallerActions.csproj"));

        Assert.Equal("$(Dhadgar.Agent.Windows.InstallerActions.TargetDir)Dhadgar.Agent.Windows.InstallerActions.CA.dll",
            (string?)binary.Attribute("SourceFile"));
        Assert.Single(LoadWixproj().Descendants("ProjectReference"),
            r => ((string?)r.Attribute("Include") ?? string.Empty)
                .EndsWith(@"\Dhadgar.Agent.Windows.InstallerActions\Dhadgar.Agent.Windows.InstallerActions.csproj",
                    System.StringComparison.Ordinal));
        // MakeSfxCA: the x64 host comes from Platform; the managed assembly must stay AnyCPU.
        Assert.Equal("net462", csproj.Descendants("TargetFramework").Single().Value);
        Assert.Equal("x64", csproj.Descendants("Platform").Single().Value);
        Assert.Equal("AnyCPU", csproj.Descendants("PlatformTarget").Single().Value);
    }

    [Fact]
    public void InstallerActions_NeverLogTheToken()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Agents",
            "Dhadgar.Agent.Windows.InstallerActions", "EnrollmentTokenActions.cs"));
        var calls = Regex.Matches(source, @"session\.Log\((?<args>(?:""(?:[^""\\]|\\.)*""|[^;""])*)\);");

        Assert.NotEmpty(calls);
        // Every session.Log( must be parsed; a call the pattern cannot read must fail the test, not be skipped.
        Assert.Equal(Regex.Count(source, @"session\.Log\("), calls.Count);
        foreach (Match call in calls)
        {
            // Strip string literals ("token written." is fine); what is left must not use the token variable.
            var code = Regex.Replace(call.Groups["args"].Value, @"""(?:[^""\\]|\\.)*""", string.Empty);
            Assert.DoesNotMatch(@"\btoken\b", code);
        }
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
