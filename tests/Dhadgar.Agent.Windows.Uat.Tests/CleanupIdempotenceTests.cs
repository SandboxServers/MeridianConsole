using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// D-S243: a second cleanup of the same manifest is clean even when an earlier pass removed the folder a file was in.
/// These tests run the real <see cref="CleanupEngine"/> over the real <see cref="WindowsMachineActions"/> against a temp repo.
/// </summary>
public sealed class CleanupIdempotenceTests : IDisposable
{
    private const string RunId = "20261027-183915-637c";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-idem-" + Guid.NewGuid().ToString("N"));

    [SkippableFact]
    public async Task UndoAsync_TlsFolderAndPfxRunTwiceWithRealActions_SecondPassIsClean()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var paths = new RunPaths(_repo, RunId);
        var pfx = Path.Combine(paths.Tls, "gateway.pfx");
        Directory.CreateDirectory(paths.Tls);
        await File.WriteAllBytesAsync(pfx, [1, 2, 3]);
        // The manifest order the driver writes: the folder first, then the file in it.
        CleanupEntry[] entries = [new DirectoryEntry(paths.Tls, ExistedBefore: false), new FileEntry(pfx)];
        var engine = new CleanupEngine(new WindowsMachineActions(), paths);

        var first = await engine.UndoAsync(entries, CancellationToken.None);
        var second = await engine.UndoAsync(entries, CancellationToken.None);

        Assert.Empty(first.Failed);
        Assert.False(Directory.Exists(paths.Tls));
        Assert.Empty(second.Failed);
        Assert.Equal(2, second.Undone);
    }

    [SkippableFact]
    public async Task UndoAsync_MissingFileOutsideRunFolder_IsStillRefused()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var paths = new RunPaths(_repo, RunId);
        var outside = Path.Combine(_repo, "elsewhere", "tls", "gateway.pfx");   // under the repo, outside the run folder

        var report = await new CleanupEngine(new WindowsMachineActions(), paths)
            .UndoAsync([new FileEntry(outside)], CancellationToken.None);

        var failure = Assert.Single(report.Failed);
        Assert.Equal("file", failure.Kind);
        Assert.Contains("refusing to delete outside the run folder", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, report.Undone);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }
}
