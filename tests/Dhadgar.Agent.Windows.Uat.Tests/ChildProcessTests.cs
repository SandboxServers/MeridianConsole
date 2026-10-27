using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

[Collection("Console")]
public class ChildProcessTests
{
    [SkippableFact]
    public void Start_LogsPathIsAFile_FailsAndStartsNoChild()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var dir = Path.Combine(Path.GetTempPath(), "uat-child-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifestPath = Path.Combine(dir, "cleanup-manifest.jsonl");
            File.WriteAllText(Path.Combine(dir, "logs"), "a file where the logs folder must be");
            var manifest = new CleanupManifest(manifestPath);
            var job = JobObject.Create();
            Assert.True(job.IsSuccess);
            using var jobObject = job.Value;

            var result = ChildProcess.Start(Spec(dir), jobObject, new Redactor(), manifest);

            Assert.True(result.IsFailure);
            Assert.False(File.Exists(manifestPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Dispose_Job_KillsAssignedChild()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var dir = Path.Combine(Path.GetTempPath(), "uat-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifestPath = Path.Combine(dir, "cleanup-manifest.jsonl");
            var manifest = new CleanupManifest(manifestPath);
            var job = JobObject.Create();
            Assert.True(job.IsSuccess);
            var jobObject = job.Value;

            var started = ChildProcess.Start(Spec(dir), jobObject, new Redactor(), manifest);
            Assert.True(started.IsSuccess);
            using var child = started.Value;

            var entries = CleanupManifest.Load(manifestPath);
            var pid = Assert.IsType<ProcessEntry>(Assert.Single(entries.Value)).Pid;

            jobObject.Dispose();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && ProcessIsRunning(pid))
            {
                await Task.Delay(100);
            }

            Assert.False(ProcessIsRunning(pid));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static ChildSpec Spec(string dir) => new(
        "agent",
        Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        ["/c", "ping -n 30 127.0.0.1"],
        dir,
        new Dictionary<string, string>(StringComparer.Ordinal));

    private static bool ProcessIsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
