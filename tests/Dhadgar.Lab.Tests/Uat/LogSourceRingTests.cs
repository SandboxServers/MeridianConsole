using System.Text.Json;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Tests.Processes;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// The <c>log</c> clause reads the supervisor's own ring for a running process, so a line the child printed is held.
/// </summary>
[Collection("LabProcesses")]
public sealed class LogSourceRingTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task LogClause_ReadsTheSupervisorRing_AndIsHeld()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        _ = harness.Leases.Acquire("s1", "uat", null, false, null);
        const string marker = "ring-clause-marker";
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-log", relaunch: false, "--lines", "1", "--message", marker, "--sleep-ms", "60000");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);
        bool logged = await SupervisorHarness.WaitUntilAsync(
            () => harness.Supervisor.RingFor("tc-log") is { } ring && ring.Snapshot().Any(line => line.Message.Contains(marker, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(30));
        Assert.True(logged);

        var source = new LogSource(harness.Supervisor.RingFor);
        var evaluator = new ClauseEvaluator(new IClauseSource[] { source }, harness.Redactor, TimeProvider.System);
        UatClause clause = Clause(
            "log",
            "eq",
            "/count",
            value: "1",
            fields: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["process"] = Json("\"tc-log\""),
                ["contains"] = Json("\"" + marker + "\""),
            });

        ClauseEvaluation result = await evaluator.EvaluateAsync(clause, Vars(), Window(), null, CancellationToken.None);

        Assert.Equal(ClauseState.Held, result.State);
    }

    private static UatVars Vars() => new("run1", "row1", "section1", 42, null);

    private static ClauseWindow Window() =>
        new(Start, Start, new Dictionary<string, DateTimeOffset>(), new Dictionary<string, DateTimeOffset>(), new Dictionary<string, StepOutcome>());

    private static UatClause Clause(
        string source,
        string op,
        string? pointer,
        string? value,
        Dictionary<string, JsonElement> fields) =>
        new(
            Id: "c1",
            Text: "clause",
            Source: source,
            Op: op,
            Value: value is null ? null : Json(value),
            Pointer: pointer,
            Since: "row_start",
            TimeoutMs: 0,
            PollMs: 500,
            Required: true,
            SourceFields: fields);

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
}
