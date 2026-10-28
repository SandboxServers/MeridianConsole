using Dhadgar.Lab.Jobs;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Jobs;

public sealed class GatewayPacerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GatewayPacer_26thCallIn60s_Waits()
    {
        var time = new FakeTimeProvider(Start);
        var pacer = new GatewayPacer(time);

        for (int i = 0; i < GatewayPacer.Limit; i++)
        {
            Task<Result> granted = pacer.WaitAsync(CancellationToken.None).AsTask();
            Assert.True(granted.IsCompletedSuccessfully);
            Assert.True((await granted).IsSuccess);
        }

        Task<Result> waiting = pacer.WaitAsync(CancellationToken.None).AsTask();
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        // The 26th call is granted once the first grant is 60 s old.
        time.Advance(GatewayPacer.Window);
        Result result = await waiting;
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GatewayPacer_GrantsAgainAfterWindowPasses()
    {
        var time = new FakeTimeProvider(Start);
        var pacer = new GatewayPacer(time);
        for (int i = 0; i < GatewayPacer.Limit; i++)
        {
            _ = await pacer.WaitAsync(CancellationToken.None);
        }

        time.Advance(TimeSpan.FromSeconds(61));

        Task<Result> again = pacer.WaitAsync(CancellationToken.None).AsTask();
        Assert.True(again.IsCompletedSuccessfully);
        Assert.True((await again).IsSuccess);
    }

    [Fact]
    public async Task GatewayPacer_CancelledWhileWaiting_ReturnsFailure()
    {
        var time = new FakeTimeProvider(Start);
        var pacer = new GatewayPacer(time);
        for (int i = 0; i < GatewayPacer.Limit; i++)
        {
            _ = await pacer.WaitAsync(CancellationToken.None);
        }

        using var cancel = new CancellationTokenSource();
        Task<Result> waiting = pacer.WaitAsync(cancel.Token).AsTask();
        await cancel.CancelAsync();

        Result result = await waiting;
        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Cancelled]", result.Error, StringComparison.Ordinal);
    }
}
