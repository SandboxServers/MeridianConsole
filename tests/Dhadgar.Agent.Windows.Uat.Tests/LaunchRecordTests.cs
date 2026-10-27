using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public sealed class LaunchRecordTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-launch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [Fact]
    public void LaunchRecord_WriteThenLoad_RoundTrips()
    {
        var paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(paths.RunDir);
        var started = new DateTime(2026, 10, 27, 14, 22, 33, DateTimeKind.Utc);
        var record = new LaunchRecord(1, RunId, "run", "process", "agent", true, "0123456789abcdef0123456789abcdef01234567", started);

        var written = LaunchRecord.Write(paths, record);
        var loaded = LaunchRecord.Load(paths);

        Assert.True(written.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(record, loaded.Value);
        var json = File.ReadAllText(Path.Combine(paths.RunDir, LaunchRecord.FileName));
        Assert.Contains("\"runId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"launchedBy\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchRecord_LoadMissing_IsFailureNotNull()
    {
        var paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(paths.RunDir);

        var loaded = LaunchRecord.Load(paths);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("[Launch.Missing]", loaded.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchRecord_WriteExisting_FailsAndKeepsFile()
    {
        var paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(paths.RunDir);
        var started = DateTime.UtcNow;
        var first = new LaunchRecord(1, RunId, "run", "process", "user", true, "first", started);
        var second = new LaunchRecord(1, RunId, "run", "process", "agent", true, "second", started);
        Assert.True(LaunchRecord.Write(paths, first).IsSuccess);

        var again = LaunchRecord.Write(paths, second);

        Assert.False(again.IsSuccess);
        var loaded = LaunchRecord.Load(paths);
        Assert.True(loaded.IsSuccess);
        Assert.Equal("first", loaded.Value.GitSha);
        Assert.Empty(Directory.GetFiles(paths.RunDir, "*.tmp"));
    }

    [Fact]
    public void LaunchRecord_LoadUnknownLaunchedBy_IsInvalid()
    {
        var paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(paths.RunDir);
        File.WriteAllText(
            Path.Combine(paths.RunDir, LaunchRecord.FileName),
            "{\"schemaVersion\":1,\"runId\":\"" + RunId + "\",\"verb\":\"run\",\"mode\":\"process\",\"launchedBy\":\"root\",\"elevated\":true,\"gitSha\":\"x\",\"startedUtc\":\"2026-10-27T14:22:33Z\"}");

        var loaded = LaunchRecord.Load(paths);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("[Launch.Invalid]", loaded.Error, StringComparison.Ordinal);
    }
}
