using System.Collections.Concurrent;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;

namespace Dhadgar.Lab.Tests.Processes;

[Collection("LabProcesses")]
public sealed class RawLineObserverTests
{
    private const string Secret = "hunter2-correct-horse";

    [SkippableFact]
    public async Task Observer_SeesRawSecret_RingAndFileSeeOnlyTheReference()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        var observer = new RecordingObserver();
        using var harness = new SupervisorHarness(observer);
        string reference = harness.Vault.Add(SecretKind.Password, Secret.AsSpan());
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-observe", relaunch: true, "--lines", "1", "--message", "db password " + Secret, "--sleep-ms", "60000");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);
        bool captured = await SupervisorHarness.WaitUntilAsync(() => (harness.Supervisor.ReadLines("tc-observe").Value?.Count ?? 0) >= 1, TimeSpan.FromSeconds(30));
        Assert.True(captured);
        _ = await harness.Supervisor.StopAsync("tc-observe");

        Assert.Contains(observer.Lines, line => line.Contains(Secret, StringComparison.Ordinal));
        string ring = string.Join("\n", harness.Supervisor.ReadLines("tc-observe").Value!.Select(line => line.Message));
        Assert.DoesNotContain(Secret, ring, StringComparison.Ordinal);
        Assert.Contains("***" + reference, ring, StringComparison.Ordinal);

        string file = await File.ReadAllTextAsync(Path.Combine(harness.Paths.Logs, "tc-observe.jsonl"));
        Assert.DoesNotContain(Secret, file, StringComparison.Ordinal);
        Assert.Contains("***" + reference, file, StringComparison.Ordinal);
    }

    private sealed class RecordingObserver : IRawLineObserver
    {
        public ConcurrentBag<string> Lines { get; } = [];

        public void Observe(string process, ReadOnlySpan<char> rawLine) => Lines.Add(rawLine.ToString());
    }
}
