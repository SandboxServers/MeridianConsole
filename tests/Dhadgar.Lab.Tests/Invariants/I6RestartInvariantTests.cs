using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

public sealed class I6RestartInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0, T0.AddMinutes(1), null, null);

    [Fact]
    public async Task I6_FirstCheck_TakesBaselineWithoutViolation()
    {
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "nodes", 3, T0.AddHours(-1)) };
        var check = new I6RestartInvariant(_ => Task.FromResult(Result<IReadOnlyList<RestartObservation>>.Success(containers)),
            () => [], () => []);

        Assert.Empty((await check.CheckAsync(Window, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_RestartExplainedByKillOnSameTarget_NoViolation()
    {
        var containers = new List<RestartObservation> { new("nodes", "nodes", 0, T0.AddHours(-1)) };
        var excuses = new List<RestartExcuse> { new("fault_kill", "c:nodes", T0.AddSeconds(10)) };
        var check = Build(containers, excuses, []);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("nodes", "nodes", 1, T0.AddSeconds(20));
        Assert.Empty((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_UnexplainedRestart_Violation()
    {
        var containers = new List<RestartObservation> { new("nodes", "nodes", 0, T0.AddHours(-1)) };
        var check = Build(containers, [], []);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("nodes", "nodes", 1, T0.AddSeconds(20));
        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
        Assert.Equal("I6", violation.InvariantId);
        Assert.Equal("restart", violation.CheckId);
    }

    [Fact]
    public async Task I6_RestartDuringDependencyFault_Violation()
    {
        var containers = new List<RestartObservation> { new("nodes", "nodes", 0, T0.AddHours(-1)) };
        var excuses = new List<RestartExcuse> { new("fault_pause", "c:nodes-postgres", T0.AddSeconds(5)) };
        var check = Build(containers, excuses, []);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("nodes", "nodes", 1, T0.AddSeconds(20));
        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_UnexpectedProcessExit_Violation_ExpectedExitIgnored()
    {
        var exits = new List<ProcessExit>
        {
            new("nodes", 1, false, T0.AddSeconds(30)),
            new("gateway", 0, true, T0.AddSeconds(30)),
        };
        var check = Build([], [], exits);

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal("process_exit", violation.CheckId);
        Assert.Equal("nodes", violation.Service);
    }

    [Fact]
    public async Task I6_ObserveFails_ResultIsFailure()
    {
        var check = new I6RestartInvariant(_ => Task.FromResult(Result<IReadOnlyList<RestartObservation>>.Failure("docker down")),
            () => [], () => []);

        Assert.True((await check.CheckAsync(Window, CancellationToken.None)).IsFailure);
    }

    private static I6RestartInvariant Build(
        List<RestartObservation> containers,
        List<RestartExcuse> excuses,
        List<ProcessExit> exits) =>
        new(_ => Task.FromResult(Result<IReadOnlyList<RestartObservation>>.Success(containers.ToArray())),
            () => excuses.ToArray(),
            () => exits.ToArray());
}
