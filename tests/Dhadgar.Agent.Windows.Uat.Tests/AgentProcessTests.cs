using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

[Collection("Console")]
public class AgentProcessTests
{
    [SkippableFact]
    public async Task StopThenStart_ReopensLogKeepsItAndReadsWhileRunning()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        var repo = Path.Combine(Path.GetTempPath(), "uat-agentproc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new RunPaths(repo, "run-1");
            Directory.CreateDirectory(paths.RunDir);
            var manifest = new CleanupManifest(paths.Manifest);
            var job = JobObject.Create();
            Assert.True(job.IsSuccess);
            using var jobObject = job.Value;
            var spec = new ChildSpec(
                "agent",
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/c", "ping -n 30 127.0.0.1"],
                paths.RunDir,
                new Dictionary<string, string>(StringComparer.Ordinal));
            using var agent = new AgentProcess(paths, jobObject, new Redactor(), manifest, spec);

            Assert.True(agent.Start().IsSuccess);
            var read = agent.SawRecoveryWarning();
            Assert.True(read.IsSuccess, read.Error);
            Assert.False(read.Value);

            Assert.True((await agent.StopAsync()).IsSuccess);
            await File.AppendAllTextAsync(Path.Combine(paths.Logs, "agent.log"), "first-run-marker" + Environment.NewLine);

            Assert.True(agent.Start().IsSuccess);
            await agent.StopAsync();

            Assert.Contains("first-run-marker", await File.ReadAllTextAsync(Path.Combine(paths.Logs, "agent.log")), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repo))
            {
                Directory.Delete(repo, recursive: true);
            }
        }
    }
}
