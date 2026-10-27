using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Every manifest append in the legs happens before the machine reads that depend on it. The fake world records the
/// number of certificate and CNG key entries in the real manifest file at each call.
/// </summary>
public sealed class ManifestOrderTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3b";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-order-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public ManifestOrderTests()
    {
        _paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(_paths.AgentPublish);
        Directory.CreateDirectory(_paths.Logs);
        File.WriteAllText(Path.Combine(_paths.AgentPublish, "appsettings.json"), "{}");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [Fact]
    public async Task CertificateAndKeyEntries_AreAppendedBeforeTheReadsThatUseThem()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var world = new LegWorld(_paths, manifest);
        var legs = new AgentLegs(
            _paths,
            manifest,
            new AgentLegsDeps(world, world, world, world, world.MintAsync),
            AgentLegsTests.Options(_paths, world.OrganizationId));

        var result = await legs.RunAsync(new Dictionary<string, object?>(StringComparer.Ordinal), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var firstRead = world.Snapshots.FindIndex(s => s.Call == "store.my");
        Assert.True(firstRead >= 0);
        Assert.Equal(0, world.Snapshots[firstRead].MachineEntries);
        var later = world.Snapshots.Skip(firstRead + 1).ToList();
        Assert.NotEmpty(later);
        Assert.All(later, s => Assert.Equal(1, s.MachineEntries));
    }

    [Fact]
    public async Task CertificateEntry_CarriesTheSha1ThumbprintOfTheSeenCertificate()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var world = new LegWorld(_paths, manifest);
        var legs = new AgentLegs(
            _paths,
            manifest,
            new AgentLegsDeps(world, world, world, world, world.MintAsync),
            AgentLegsTests.Options(_paths, world.OrganizationId));

        var result = await legs.RunAsync(new Dictionary<string, object?>(StringComparer.Ordinal), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        var certificate = Assert.Single(loaded.Value.OfType<CertificateEntry>());
        Assert.Equal(world.Mine[0].Sha1Thumbprint, certificate.ThumbprintSha1);
        Assert.Equal("LocalMachine", certificate.StoreLocation);
        Assert.Equal("My", certificate.StoreName);
        Assert.Empty(loaded.Value.OfType<CngKeyEntry>());
    }
}
