using System.Diagnostics;
using System.Reflection;
using Dhadgar.Lab.Build;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Lab.Tests.Build;

public sealed class LabBuilderTests
{
    [Fact]
    public async Task RunAsync_CallerCancelled_KillsTheChildAndRethrows()
    {
        // A long-running child stands in for MSBuild. The caller's cancellation must stop the child (N2), not leave it running.
        string process = OperatingSystem.IsWindows() ? "PING" : "sleep";
        int before = Process.GetProcessesByName(process).Length;
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "ping -n 60 127.0.0.1 > nul" }
            : new List<string> { "60" };
        string fileName = OperatingSystem.IsWindows() ? "cmd" : "sleep";
        MethodInfo run = typeof(LabBuilder).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("LabBuilder.RunAsync not found");

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        var task = (Task<Result<string>>)run.Invoke(null, [fileName, args, Environment.CurrentDirectory, cancel.Token])!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        // The kill is immediate but the OS reaps the child asynchronously; allow a few seconds.
        for (int i = 0; i < 50 && Process.GetProcessesByName(process).Length > before; i++)
        {
            await Task.Delay(100);
        }

        Assert.True(Process.GetProcessesByName(process).Length <= before);
    }
}
