using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

/// <summary>
/// L22 bundle evidence: the row <c>faults</c> and <c>violations</c>, the <c>[[fault]]</c> timeline in <c>repro.toml</c>, and the
/// fidelity a timing fault forces.
/// </summary>
public sealed class FaultEvidenceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private const string FaultSpec = """
        schema = 1

        [section]
        id = "fault-evidence"
        title = "Fault evidence"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "slow"
          inject = { tool = "fault_toxic_add", args = { proxy = "p", toxic = "latency", stream = "downstream" } }
          start = "before_steps"
          heal = "after_steps"
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l22-evidence-" + Guid.NewGuid().ToString("N"));
    private readonly SecretVault _vault = new();
    private readonly BundleWriter _writer;

    public FaultEvidenceTests()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        _ = Directory.CreateDirectory(uatRuns);
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        _writer = new BundleWriter(uatRuns, validator, redactor, new SecretScanner(_vault), new FakeTimeProvider(Start), "abcd1234", "linux", "build", null);
    }

    [Fact]
    public async Task FaultRow_WritesFaultsViolationsAndReproTimeline_AndTimingFidelity()
    {
        JsonElement args = JsonDocument.Parse("{\"proxy\":\"p\",\"toxic\":\"latency\",\"stream\":\"downstream\"}").RootElement.Clone();
        var fault = new UatRowFault("slow", "fault_toxic_add", args, "f-1", Start.AddSeconds(1), Start.AddSeconds(4), null);
        var violation = new InvariantViolation("I1", "nodes", "check-I1", "detail", Start.AddSeconds(2), null);
        SpecLoadOutcome loaded = SpecLoader.LoadText("fault-evidence.toml", FaultSpec);
        Assert.NotNull(loaded.Spec);

        _writer.RunStarted("run-l22-faults", 3UL, loaded.Spec!);
        _writer.RowFinished(new UatRowResult("faulted", RowGrade.Pass, string.Empty, [], StartedAt: Start, EndedAt: Start.AddSeconds(5),
            Violations: [new UatRowViolation(violation, true)], Faults: [fault]));
        _writer.RunFinished(new UatRunResult("run-l22-faults", 3UL, false, RunOverall.Pass, []));

        Assert.Null(_writer.WriteError);
        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "rows", "fault-evidence", "faulted.json")));
        JsonElement written = Assert.Single(row.RootElement.GetProperty("faults").EnumerateArray());
        Assert.Equal("f-1", written.GetProperty("fault_id").GetString());
        Assert.Equal(JsonValueKind.Null, written.GetProperty("heal_error").ValueKind);
        Assert.Equal(Start.AddSeconds(4), written.GetProperty("healed_at").GetDateTimeOffset());
        JsonElement violations = Assert.Single(row.RootElement.GetProperty("violations").EnumerateArray());
        Assert.Equal("I1", violations.GetProperty("invariant_id").GetString());
        Assert.True(violations.GetProperty("allowed").GetBoolean());

        string repro = await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "repro", "repro.toml"));
        Assert.Contains("[[fault]]", repro, StringComparison.Ordinal);
        Assert.Contains("injected_ms = 1000", repro, StringComparison.Ordinal);
        Assert.Contains("healed_ms = 4000", repro, StringComparison.Ordinal);
        Assert.Contains("fidelity = \"schedule_approximate\"", repro, StringComparison.Ordinal);

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "run.json")));
        Assert.Equal(1, run.RootElement.GetProperty("row_violations").GetArrayLength());
    }

    [Fact]
    public async Task NonTimingFault_KeepsFidelity()
    {
        JsonElement args = JsonDocument.Parse("{\"target\":\"c:x\"}").RootElement.Clone();
        var fault = new UatRowFault("kill", "fault_kill", args, "f-2", Start, Start.AddSeconds(1), null);
        SpecLoadOutcome loaded = SpecLoader.LoadText("fault-evidence.toml", FaultSpec);
        Assert.NotNull(loaded.Spec);

        _writer.RunStarted("run-l22-kill", 3UL, loaded.Spec!);
        _writer.RowFinished(new UatRowResult("faulted", RowGrade.Pass, string.Empty, [], StartedAt: Start, Faults: [fault]));
        _writer.RunFinished(new UatRunResult("run-l22-kill", 3UL, false, RunOverall.Pass, []));

        string repro = await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "repro", "repro.toml"));
        Assert.DoesNotContain("schedule_approximate", repro, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _vault.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
