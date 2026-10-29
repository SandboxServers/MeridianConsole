using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// Wave 5 live gate: the lab's own restarts (svc_start, svc_restart, stack_up, member_reset) excuse the I6 restart they cause.
/// An unexcused restart still violates I6, and an excuse expires with the I6 window.
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
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0);

        RestartExcuse excuse = Assert.Single(excuses.Snapshot());
        Assert.Equal("svc_start", excuse.Action);
        Assert.Equal("meridian-lab-nodes", excuse.Target);
        Assert.Equal(T0, excuse.At);
    }

    [Fact]
    public void ForCall_ToolsThatRestartNothing_RecordNothing()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("db_query", Json("{\"database\":\"nodes\"}"), T0);
        excuses.RecordCall("invariants_check", Json("{}"), T0);
        excuses.RecordCall("svc_start", Json("{}"), T0);

        Assert.Empty(excuses.Snapshot());
    }

    [Fact]
    public void ForCall_StackUp_ExcusesEveryContainer()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{\"wipe\":true}"), T0);

        Assert.Equal(LabRestartExcuses.AllTargets, Assert.Single(excuses.Snapshot()).Target);
    }

    [Fact]
    public async Task Dispatcher_AcceptedSvcStart_RecordsTheExcuse()
    {
        var excuses = new LabRestartExcuses();
        LabToolDispatcher dispatcher = NewDispatcher(excuses, name => name == "svc_start" ? LabGate.Open : null);

        LabToolResult result = await dispatcher.InvokeAsync("svc_start", Json("{\"service\":\"nodes\"}"), Context(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("meridian-lab-nodes", Assert.Single(excuses.Snapshot()).Target);
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
    public async Task I6_LabDrivenSvcStartOfNodes_YieldsNoViolation()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0.AddSeconds(10));
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
        excuses.RecordCall("svc_start", Json("{\"service\":\"gateway\"}"), T0.AddSeconds(10));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 1, T0.AddSeconds(20));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_ExcuseOutsideTheWindow_Expires()
    {
        // Recorded five minutes before the window: outside the window and its 30 s margin, so it no longer excuses.
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("svc_start", Json("{\"service\":\"nodes\"}"), T0.AddMinutes(-5));
        var containers = new List<RestartObservation> { new("meridian-lab-nodes", "meridian-lab-nodes", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-nodes", "meridian-lab-nodes", 1, T0.AddSeconds(20));

        Assert.Single((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I6_StackUpExcuse_CoversAnyContainer()
    {
        var excuses = new LabRestartExcuses();
        excuses.RecordCall("stack_up", Json("{}"), T0.AddSeconds(5));
        var containers = new List<RestartObservation> { new("meridian-lab-gateway", "meridian-lab-gateway", 0, T0.AddHours(-1)) };
        I6RestartInvariant check = Build(containers, excuses);
        await check.CheckAsync(Window, CancellationToken.None);

        containers[0] = new RestartObservation("meridian-lab-gateway", "meridian-lab-gateway", 1, T0.AddSeconds(20));

        Assert.Empty((await check.CheckAsync(Window with { To = T0.AddMinutes(2) }, CancellationToken.None)).Value!);
    }

    private static I6RestartInvariant Build(List<RestartObservation> containers, LabRestartExcuses excuses) =>
        new(_ => Task.FromResult(Result<IReadOnlyList<RestartObservation>>.Success(containers.ToArray())),
            InvariantWiring.Excuses(new NoFaults(), excuses),
            () => []);

    private LabToolDispatcher NewDispatcher(LabRestartExcuses excuses, Func<string, LabGate?> gateFor) =>
        new(new LabToolRegistry([new SvcStubTool()]), _leases, _audit, _redactor, _time, gateFor, excuses);

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "SvcArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record SvcArgs(string? Service = null);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "SvcStubTool is registered in the dispatcher under the name svc_start.")]
    private sealed class SvcStubTool : LabTool<SvcArgs>
    {
        public override string Name => "svc_start";

        public override string Description => "Test stand-in for svc_start.";

        protected override Task<Result<object>> ExecuteAsync(SvcArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { started = args.Service }));
    }
}
