using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// Wave 5 live gate: the lab's own successful calls (svc_*, stack_*, member_reset) excuse the I6 restart or process exit they
/// cause, only on their own targets and only at the time they ran (D-ML114). An unexcused restart still violates I6.
/// </summary>
public sealed class LabRestartExcusesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0, T0.AddMinutes(1), null, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-wf-f-excuses-" + Guid.NewGuid().ToString("N"));
    private readonly SecretVault _vault = new();
    private readonly AuditLog _audit;
    private readonly FakeTimeProvider _time = new(T0);
    private readonly LeaseBook _leases;
    private readonly Redactor _redactor;

    public LabRestartExcusesTests()
    {
        Directory.CreateDirectory(_root);
        _redactor = new Redactor(_vault);
        _audit = new AuditLog(LabPaths.FromRoot(_root), new PathValidator(NullLogger<PathValidator>.Instance), _redactor, _time);
        _leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
    }

    public void Dispose()
    {
        _audit.Dispose();
        _vault.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ForCall_SvcStart_ExcusesTheRegistryNameOfTheService()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0, T0.AddSeconds(4));

        RestartExcuse excuse = Assert.Single(excuses.Snapshot());
        Assert.Equal("svc_start", excuse.Action);
        Assert.Equal("meridian-lab-nodes", excuse.Target);
        Assert.Equal(T0, excuse.At);
        Assert.Equal(T0.AddSeconds(4), excuse.Until);
    }

    [Fact]
    public void ForCall_ToolsThatRestartNothing_RecordNothing()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("db_query", Json("{\"database\":\"nodes\"}"), T0, T0);
        excuses.RecordCall("invariants_check", Json("{}"), T0, T0);
        excuses.RecordCall("svc_start", Json("{}"), T0, T0);

        Assert.Empty(excuses.Snapshot());
    }

    [Fact]
    public void ForCall_StackUp_ExcusesTheInfraContainers()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0, T0);
        excuses.RecordCall("stack_up", Json("{\"wipe\":false}"), T0, T0);

        string[] infra = ["meridian-lab-postgres", "meridian-lab-rabbitmq", "meridian-lab-redis", "meridian-lab-toxiproxy"];
        Assert.Equal(infra.Concat(infra), excuses.Snapshot().Select(e => e.Target));
    }

    [Fact]
    public void ForCall_StackUpWipe_And_StackDown_ExcuseAllTargets()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{\"wipe\":true}"), T0, T0);
        excuses.RecordCall("stack_down", Json("{}"), T0, T0);

        IReadOnlyList<RestartExcuse> recorded = excuses.Snapshot();
        Assert.Equal(2, recorded.Count);
        Assert.All(recorded, e => Assert.Equal(LabRestartExcuses.AllTargets, e.Target));
    }

    [Fact]
    public void ForCall_SvcStop_ExcusesTheService()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_stop", Json("{\"service\":\"nodes\"}"), T0, T0);

        Assert.Equal("meridian-lab-nodes", Assert.Single(excuses.Snapshot()).Target);
    }

    [Fact]
    public async Task Dispatcher_AcceptedSvcStart_RecordsTheExcuse()
    {
        var excuses = new LabRestartExcuses();
        LabToolDispatcher dispatcher = NewDispatcher(excuses, name => name == "svc_start" ? LabGate.Open : null);

        LabToolResult result = await dispatcher.InvokeAsync("svc_start", Json("{\"service\":\"nodes\"}"), Context(), CancellationToken.None);

        Assert.False(result.IsError);
        RestartExcuse excuse = Assert.Single(excuses.Snapshot());
        Assert.Equal("meridian-lab-nodes", excuse.Target);
        Assert.Equal(T0, excuse.At);
        Assert.Equal(T0.AddSeconds(10), excuse.Until);
    }

    [Fact]
    public async Task Dispatcher_RefusedSvcStart_RecordsNoExcuse()
    {
        // A tool with no gate is refused before it runs, so a refused call excuses nothing.
        var excuses = new LabRestartExcuses();
        LabToolDispatcher dispatcher = NewDispatcher(excuses, _ => null);

        LabToolResult result = await dispatcher.InvokeAsync("svc_start", Json("{\"service\":\"nodes\"}"), Context(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(excuses.Snapshot());
    }

    [Fact]
    public async Task Dispatcher_FailedStackUp_RecordsNoExcuse()
    {
        // security N2: a call that returns an error excuses nothing, even though it ran.
        var excuses = new LabRestartExcuses();
        var registry = new LabToolRegistry([new StubTool("stack_up", fail: true, _time)]);
        LabToolDispatcher dispatcher = NewDispatcher(excuses, name => name == "stack_up" ? LabGate.Open : null, registry);

        LabToolResult result = await dispatcher.InvokeAsync("stack_up", Json("{}"), Context(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(excuses.Snapshot());
    }

    [Fact]
    public async Task I6_LabDrivenSvcStartOfNodes_YieldsNoViolation()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0.AddSeconds(10), T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddSeconds(20));

        Assert.Empty((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_UnexcusedRestart_StillViolates()
    {
        // The anti-always-excuse check: no lab call, so the restart is unexplained.
        var excuses = new LabRestartExcuses();
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 1, T0.AddSeconds(20));

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
        Assert.Equal("I6", violation.InvariantId);
        Assert.Equal("restart", violation.CheckId);
    }

    [Fact]
    public async Task I6_ExcuseForAnotherService_DoesNotExcuseNodes()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"gateway\"}"), T0.AddSeconds(10), T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 1, T0.AddSeconds(20));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_ExcuseLongBeforeTheRestart_Expires()
    {
        // Recorded five minutes before the restart: the restart is outside the excuse's window and its margin, so it no longer excuses.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0.AddMinutes(-5), T0.AddMinutes(-5).AddSeconds(5));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddSeconds(20));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpExcuse_CoversTheInfraContainers()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0.AddSeconds(5), T0.AddSeconds(15));
        var containers = new List<RestartObservation> { new("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddSeconds(20));

        Assert.Empty((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpExcuse_DoesNotExcuseNodes()
    {
        // A plain stack_up creates only the infra containers, so it does not excuse a nodes restart.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0.AddSeconds(5), T0.AddSeconds(15));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddSeconds(10));

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
        Assert.Equal("restart", violation.CheckId);
    }

    [Fact]
    public async Task I6_StackUpExcuse_RestartAfterTheCall_Violates()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0, T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddSeconds(90));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpExcuse_CountRiseDuringTheCall_Violates()
    {
        // security N1a: a restart policy raises the count, and a stack_up never excuses a count rise.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0, T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-postgres", "meridian-lab-postgres", 1, T0.AddSeconds(5));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpExcuse_RecreateDuringTheCall_NoViolation()
    {
        // A recreate moves StartedAt and leaves the count, so the call excuses it.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0, T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-postgres", "meridian-lab-postgres", 0, T0.AddSeconds(5));

        Assert.Empty((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpWipe_FollowerExitDuringTheCall_NoViolation()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{\"wipe\":true}"), T0, T0.AddSeconds(10));
        var exits = new List<ProcessExit> { new("nodes", 0, false, T0.AddSeconds(5)) };
        I6RestartInvariant check = Build([], excuses, exits);

        Assert.Empty((await check.CheckAsync(Window, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpWipe_ExitAfterTheCall_Violates()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{\"wipe\":true}"), T0, T0.AddSeconds(10));
        var exits = new List<ProcessExit> { new("nodes", 0, false, T0.AddSeconds(90)) };
        I6RestartInvariant check = Build([], excuses, exits);

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
        Assert.Equal("process_exit", violation.CheckId);
    }

    [Fact]
    public async Task I6_SvcStart_FollowerExitOfTheService_NoViolation()
    {
        // The container log follower is named with the short service name; it is the same service as the registry name.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0, T0.AddSeconds(10));
        var exits = new List<ProcessExit> { new("nodes", 0, false, T0.AddSeconds(3)) };
        I6RestartInvariant check = Build([], excuses, exits);

        Assert.Empty((await check.CheckAsync(Window, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_SvcStart_LaterCrashOfTheService_Violates()
    {
        // review D2: an earlier call does not excuse a later crash of the same service inside the same window.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0, T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddSeconds(50));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_SvcRestart_CountRise_StillViolates()
    {
        // Rule 3: a restart policy raises the count, and a svc_restart never excuses a count rise.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_restart", Json("{\"service\":\"nodes\"}"), T0, T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 1, T0.AddSeconds(5));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_LongSvcRestart_StartNearItsEnd_NoViolation()
    {
        // The call runs for two minutes; the restart at its end is inside the call's window and its margin.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_restart", Json("{\"service\":\"nodes\"}"), T0, T0.AddSeconds(120));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddSeconds(115));

        Assert.Empty((await check.CheckAsync(Window with { To = T0.AddMinutes(3) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_LongSvcRestart_StartBeforeTheWindow_StillExcuses()
    {
        // The call started before the next window opens and ends inside it, so its excuse still covers the restart.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_restart", Json("{\"service\":\"nodes\"}"), T0, T0.AddSeconds(120));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddSeconds(100));

        var next = new InvariantWindow(T0.AddMinutes(1), T0.AddMinutes(2), null, null);
        Assert.Empty((await check.CheckAsync(next, CancellationToken.None)).Value!);
    }

    private static I6RestartInvariant Build(
        List<RestartObservation> containers,
        LabRestartExcuses excuses,
        List<ProcessExit>? exits = null) =>
        new(_ => Task.FromResult(Result<IReadOnlyList<RestartObservation>>.Success(containers.ToArray())),
            InvariantWiring.Excuses(new NoFaults(), excuses),
            () => (exits ?? []).ToArray());

    private LabToolDispatcher NewDispatcher(LabRestartExcuses excuses, Func<string, LabGate?> gateFor, LabToolRegistry? registry = null) =>
        new(registry ?? new LabToolRegistry([new StubTool("svc_start", fail: false, _time)]), _leases, _audit, _redactor, _time, gateFor, excuses);

    private static LabCallContext Context() => new(Guid.NewGuid().ToString("N"), null, "test");

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private sealed class NoFaults : IFaultHistory
    {
        public IReadOnlyList<FaultHistoryEntry> Snapshot() => [];
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "StubArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record StubArgs(string? Service = null, bool? Wipe = null);

    /// <summary>
    /// A stand-in for a lab tool: it takes ten seconds of fake time, then succeeds or fails.
    /// </summary>
    private sealed class StubTool : LabTool<StubArgs>
    {
        private readonly string _name;
        private readonly bool _fail;
        private readonly FakeTimeProvider _time;

        public StubTool(string name, bool fail, FakeTimeProvider time)
        {
            _name = name;
            _fail = fail;
            _time = time;
        }

        public override string Name => _name;

        public override string Description => "Test stand-in for a lab tool.";

        protected override Task<Result<object>> ExecuteAsync(StubArgs args, LabCallContext context, CancellationToken ct)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            return Task.FromResult(_fail
                ? Result<object>.Failure("[Lab.InvalidArgument] stub failure")
                : Result<object>.Success(new { started = args.Service }));
        }
    }
}
