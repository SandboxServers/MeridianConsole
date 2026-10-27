using System.Xml.Linq;
using Dhadgar.Agent.Windows.Uat;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The driver's copies of the MSI constants, pinned against the installer source (P23). Plain facts that read
/// <c>Package.wxs</c>, so they run on Linux CI, where the MSI is never built. A rename on either side fails a test.
/// </summary>
public sealed class MsiFactsTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";

    private const string PackageFile = "src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dhadgar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Dhadgar.sln not found above the test output folder.");
    }

    private static XDocument LoadPackage() => XDocument.Load(Path.Combine(RepoRoot(), PackageFile));

    [Fact]
    public void UpgradeCode_EqualsThePackageUpgradeCode()
    {
        var package = LoadPackage().Descendants(Wix + "Package").Single();

        Assert.Equal(AgentWindowsFacts.MsiUpgradeCode, (string?)package.Attribute("UpgradeCode"), ignoreCase: true);
    }

    [Fact]
    public void PublicPropertyNames_ExistInThePackage()
    {
        var properties = LoadPackage().Descendants(Wix + "Property").Select(p => (string?)p.Attribute("Id")).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(AgentWindowsFacts.MsiTokenProperty, properties);
        Assert.Contains(AgentWindowsFacts.MsiEndpointProperty, properties);
    }

    [Fact]
    public void ControlPlaneEndpointValueName_EqualsTheRegistryValueTheInstallerWrites()
    {
        // The agent reads AgentRegistry.ControlPlaneEndpointValueName; this is the installer side of that name.
        var names = LoadPackage().Descendants(Wix + "RegistryValue").Select(v => (string?)v.Attribute("Name")).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(AgentWindowsFacts.ControlPlaneEndpointValueName, names);
    }
}
