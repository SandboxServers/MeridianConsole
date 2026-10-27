using Dhadgar.Agent.Windows.Uat.Cleanup;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public sealed class CleanupManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uat-manifest-" + Guid.NewGuid().ToString("N"));

    public CleanupManifestTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AppendAndLoad_EveryKind_RoundTrips()
    {
        var path = Path.Combine(_dir, "cleanup-manifest.jsonl");
        var manifest = new CleanupManifest(path);
        var when = new DateTime(2026, 10, 27, 14, 22, 33, DateTimeKind.Utc);
        CleanupEntry[] entries =
        [
            new ContainerEntry("meridian-uat-pg-20261027-142233-9f3a"),
            new ProcessEntry(4242, when, "nodes"),
            new RegistryKeyEntry("HKLM", @"SOFTWARE\Meridian Console\Agent", true),
            new RegistryValueEntry(@"SOFTWARE\Meridian Console\Agent", "EnrollmentToken", false),
            new CertificateEntry("LocalMachine", "Root", "ABCDEF0123"),
            new CngKeyEntry("uat-key", "Microsoft Software Key Storage Provider", true),
            new CertStoreEntry("MeridianConsoleAgent", false),
            new DirectoryEntry(Path.Combine(_dir, "agent"), false),
            new FileEntry(Path.Combine(_dir, "agent", "appsettings.json")),
            new EventSourceEntry("Meridian Console Agent", false),
            new ServiceEntry("DhadgarAgent"),
        ];

        foreach (var entry in entries)
        {
            Assert.True(manifest.Append(entry).IsSuccess);
        }

        var loaded = CleanupManifest.Load(path);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(entries, loaded.Value);
    }

    [Fact]
    public void Load_TruncatedLastLine_SkipsIt()
    {
        var path = Path.Combine(_dir, "cleanup-manifest.jsonl");
        var manifest = new CleanupManifest(path);
        Assert.True(manifest.Append(new ServiceEntry("DhadgarAgent")).IsSuccess);
        File.AppendAllText(path, "{\"kind\":\"file\",\"path\":");

        var loaded = CleanupManifest.Load(path);

        Assert.True(loaded.IsSuccess);
        Assert.Equal([new ServiceEntry("DhadgarAgent")], loaded.Value);
    }
}
