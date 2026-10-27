using System.Diagnostics;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class WindowsMachineActionsTests
{
    [SkippableFact]
    public void GetProcessImage_OwnProcess_ReturnsRunningWithItsImage()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var self = Process.GetCurrentProcess();

        var result = new WindowsMachineActions().GetProcessImage(self.Id, self.StartTime.ToUniversalTime());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsRunning);
        Assert.Equal(Environment.ProcessPath, result.Value.Path, ignoreCase: true);
    }

    [SkippableFact]
    public void GetProcessImage_ReusedPid_ReturnsNotRunning()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        using var self = Process.GetCurrentProcess();

        var result = new WindowsMachineActions().GetProcessImage(self.Id, self.StartTime.ToUniversalTime().AddHours(-1));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsRunning);
    }

    [SkippableFact]
    public void GetProcessImage_NoSuchPid_ReturnsNotRunning()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");

        // Windows process IDs are multiples of 4, so PID 3 never exists.
        var result = new WindowsMachineActions().GetProcessImage(3, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsRunning);
    }
}
