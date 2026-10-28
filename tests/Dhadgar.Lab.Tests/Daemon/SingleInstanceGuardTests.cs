using Dhadgar.Lab.Daemon;

namespace Dhadgar.Lab.Tests.Daemon;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Acquire_SecondAcquireWhileFirstHeld_RefusedAsAlreadyRunning()
    {
        var root = NewRoot();
        try
        {
            var paths = LabPaths.FromRoot(root);
            var first = SingleInstanceGuard.Acquire(paths);
            Assert.True(first.IsSuccess, first.Error);

            using (first.Value)
            {
                var second = SingleInstanceGuard.Acquire(paths);

                Assert.True(second.IsFailure);
                Assert.StartsWith("[Lab.AlreadyRunning]", second.Error, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Acquire_WhileHeld_WritesPidFile()
    {
        var root = NewRoot();
        try
        {
            var paths = LabPaths.FromRoot(root);
            var guard = SingleInstanceGuard.Acquire(paths);
            Assert.True(guard.IsSuccess, guard.Error);

            using (guard.Value)
            {
                Assert.True(File.Exists(paths.PidFile));
                using var reader = new FileStream(paths.PidFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var text = new StreamReader(reader);
                Assert.Equal(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), text.ReadToEnd());
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Dispose_RemovesPidFile()
    {
        var root = NewRoot();
        try
        {
            var paths = LabPaths.FromRoot(root);
            var guard = SingleInstanceGuard.Acquire(paths);
            Assert.True(guard.IsSuccess, guard.Error);

            guard.Value.Dispose();

            Assert.False(File.Exists(paths.PidFile));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Acquire_AfterDispose_Succeeds()
    {
        var root = NewRoot();
        try
        {
            var paths = LabPaths.FromRoot(root);
            var first = SingleInstanceGuard.Acquire(paths);
            Assert.True(first.IsSuccess, first.Error);
            first.Value.Dispose();

            var again = SingleInstanceGuard.Acquire(paths);

            Assert.True(again.IsSuccess, again.Error);
            again.Value.Dispose();
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task StartAsync_SecondDaemonWithSingleInstance_RefusedAsAlreadyRunning()
    {
        var root = NewRoot();
        try
        {
            var token = EdgeTestHost.NewToken();
            var options = new LabDaemonOptions(0, token, root, EnforceSingleInstance: true, RefuseElevated: false);

            await using var first = (await LabDaemon.StartAsync(options, CancellationToken.None)).Value;
            var second = await LabDaemon.StartAsync(options, CancellationToken.None);

            Assert.True(second.IsFailure);
            Assert.StartsWith("[Lab.AlreadyRunning]", second.Error, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Acquire_OnLinux_RootIsUserPrivateAndPidFileIsUserOnly()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // The root is nested so that Acquire creates it; an existing directory keeps its own mode.
        var outer = Path.Combine(Path.GetTempPath(), "lab-l02-mode-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(outer, "root");
        try
        {
            var paths = LabPaths.FromRoot(root);
            var guard = SingleInstanceGuard.Acquire(paths);
            Assert.True(guard.IsSuccess, guard.Error);

            using (guard.Value)
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(paths.PidFile));
            }
        }
        finally
        {
            DeleteRoot(outer);
        }
    }

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "lab-l02-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
