using Dhadgar.Agent.Windows.Uat.Results;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class StepRecorderTests
{
    [Fact]
    public async Task RunAsync_MapsATransportFailureToInconclusive_NeverFail()
    {
        var recorder = new StepRecorder();

        var step = await recorder.RunAsync(
            "U7",
            "grace over the wire",
            _ => Task.FromResult(Result<Dictionary<string, object?>>.Failure("U7: [Probe.Transport] HttpRequestException")),
            CancellationToken.None);

        Assert.Equal("inconclusive", step.Status);
    }

    [Fact]
    public async Task RunAsync_MapsAnOtherFailureToFail()
    {
        var recorder = new StepRecorder();

        var step = await recorder.RunAsync(
            "U7",
            "grace over the wire",
            _ => Task.FromResult(Result<Dictionary<string, object?>>.Failure("U7: x")),
            CancellationToken.None);

        Assert.Equal("fail", step.Status);
    }
}
