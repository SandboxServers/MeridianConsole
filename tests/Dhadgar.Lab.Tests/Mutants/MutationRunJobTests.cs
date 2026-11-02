using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tests.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Mutants;

/// <summary>
/// The <c>mutation</c> job (L54): verdicts, the restore in <c>finally</c>, cancellation and the result files. Every tool call is a fake;
/// nothing here starts a container.
/// </summary>
public sealed class MutationRunJobTests : IDisposable
{
    private const string Lease = "lease-test-7f3a91c2";
    private const string PlainBuild = "plain-build-0001";
    private const string Pattern = "^hist:enrollment\\|E1\\|enroll-race$";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dhadgar-mutation-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly SecretVault _vault = new();

    [Fact]
    public async Task Detected_when_the_expected_signature_appears()
    {
        var fake = new FakeInvoker { HistoryPayload = """{"run_id":"r1","violations":[{"rule":"E1"}]}""" };

        JobOutcome outcome = await RunAsync([Spec("M1")], fake);

        Assert.Equal("complete", outcome.Overall);
        Assert.Equal("detected", Verdict(outcome, "M1"));
        Assert.Equal(1, fake.Count("history_run"));
        Assert.Equal(1, Starts(fake, "build-M1"));
        Assert.Equal(1, Starts(fake, PlainBuild));
        Assert.True(outcome.Result!["restore"]!["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Detected_other_when_only_another_signature_appears_after_all_attempts()
    {
        var fake = new FakeInvoker { HistoryPayload = """{"run_id":"r2","violations":[{"rule":"E9"}]}""" };

        JobOutcome outcome = await RunAsync([Spec("M2")], fake);

        Assert.Equal("detected_other", Verdict(outcome, "M2"));
        Assert.Equal(MutationRunJob.ScheduleApproximateAttempts, fake.Count("history_run"));
        Assert.NotNull(Mutant(outcome, "M2")["reason"]);
    }

    [Fact]
    public async Task Survived_when_no_violation_appears()
    {
        var fake = new FakeInvoker { HistoryPayload = """{"run_id":"r3","violations":[]}""" };

        JobOutcome outcome = await RunAsync([Spec("M3")], fake);

        Assert.Equal("survived", Verdict(outcome, "M3"));
        Assert.Equal(MutationRunJob.ScheduleApproximateAttempts, fake.Count("history_run"));
        Assert.Equal(1, Starts(fake, PlainBuild));
    }

    [Fact]
    public async Task Stale_patch_is_reported_without_starting_anything()
    {
        var fake = new FakeInvoker();

        JobOutcome outcome = await RunAsync(
            [Spec("M4")],
            fake,
            build: (spec, ct) => Task.FromResult(Result<MutantBuildDto>.Success(
                new MutantBuildDto(spec.Id, MutantBuilder.StaleVerdict, "git apply --check rejected the patch", "", "", [], [], _time.GetUtcNow()))));

        Assert.Equal(MutantBuilder.StaleVerdict, Verdict(outcome, "M4"));
        Assert.Empty(fake.Calls);
        Assert.True(outcome.Result!["restore"]!["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Build_failure_is_unreachable_and_runs_nothing()
    {
        var fake = new FakeInvoker();

        JobOutcome outcome = await RunAsync(
            [Spec("M5")],
            fake,
            build: (spec, ct) => Task.FromResult(Result<MutantBuildDto>.Failure("[Lab.ProcessFailed] docker build exited 1")));

        Assert.Equal("unreachable", Verdict(outcome, "M5"));
        Assert.StartsWith("build failed", Mutant(outcome, "M5")["reason"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Service_start_failure_is_unreachable_and_the_start_is_still_restored()
    {
        var fake = new FakeInvoker { FailStartBuild = "build-M6" };

        JobOutcome outcome = await RunAsync([Spec("M6")], fake);

        Assert.Equal("unreachable", Verdict(outcome, "M6"));
        Assert.Equal(1, Starts(fake, PlainBuild));
        Assert.Equal("complete", outcome.Overall);
    }

    [Fact]
    public async Task Detector_error_is_unreachable_and_the_plain_build_is_restored()
    {
        var fake = new FakeInvoker { HistoryError = true };

        JobOutcome outcome = await RunAsync([Spec("M7")], fake);

        Assert.Equal("unreachable", Verdict(outcome, "M7"));
        Assert.Equal(1, Starts(fake, PlainBuild));
        Assert.True(outcome.Result!["restore"]!["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Cancel_inside_the_detector_ends_cancelled_and_still_restores()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeInvoker
        {
            HistoryPayload = """{"run_id":"r8","violations":[]}""",
            OnCall = tool =>
            {
                if (tool == "history_run")
                {
                    cts.Cancel();
                }
            },
        };

        JobOutcome outcome = await RunAsync([Spec("M8")], fake, ct: cts.Token);

        Assert.Equal("cancelled", outcome.Overall);
        Assert.Equal(1, Starts(fake, PlainBuild));
        Assert.True(outcome.Result!["restore"]!["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Started_mutant_without_a_plain_build_is_restore_failed()
    {
        var fake = new FakeInvoker { HistoryPayload = """{"run_id":"r9","violations":[{"rule":"E1"}]}""" };

        JobOutcome outcome = await RunAsync([Spec("M9")], fake, plain: null);

        Assert.Equal("restore_failed", outcome.Overall);
        Assert.False(outcome.Result!["restore"]!["ok"]!.GetValue<bool>());
        Assert.Contains("no plain build", outcome.Result["restore"]!["reason"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_DetectorCall_SeesMarkSet_ThenCleared()
    {
        var running = new RunningMutantBuild();
        MutantMark? seen = null;
        var fake = new FakeInvoker
        {
            OnCall = tool =>
            {
                if (tool == "history_run")
                {
                    seen = running.Current;
                }
            },
        };

        JobOutcome outcome = await RunAsync([Spec("M1")], fake, MutantBuildWithStamp(), running: running);

        Assert.Equal("complete", outcome.Overall);
        Assert.NotNull(seen);
        Assert.Equal("M1", seen!.MutantId);
        Assert.Equal("mutant-M1-bbbbbbbb", seen.BuildId);
        Assert.Equal(new string('b', 40), seen.Commit);
        Assert.Null(running.Current);
    }

    [Fact]
    public async Task RunAsync_RestoreFails_MarkKept()
    {
        var running = new RunningMutantBuild();
        var fake = new FakeInvoker
        {
            HistoryPayload = """{"run_id":"r11","violations":[{"rule":"E1"}]}""",
            FailStartBuild = PlainBuild,
        };

        JobOutcome outcome = await RunAsync([Spec("M11")], fake, MutantBuildWithStamp(), running: running);

        Assert.Equal("restore_failed", outcome.Overall);
        MutantMark? mark = running.Current;
        Assert.NotNull(mark);
        Assert.Equal("M11", mark!.MutantId);
        Assert.Equal("mutant-M11-bbbbbbbb", mark.BuildId);
    }

    [Fact]
    public async Task Failed_restore_start_is_reported()
    {
        var fake = new FakeInvoker { HistoryPayload = """{"run_id":"r10","violations":[{"rule":"E1"}]}""", FailStartBuild = PlainBuild };

        JobOutcome outcome = await RunAsync([Spec("M10")], fake);

        Assert.Equal("restore_failed", outcome.Overall);
        Assert.False(outcome.Result!["restore"]!["ok"]!.GetValue<bool>());
        Assert.Contains("plain build restore failed", outcome.Result["restore"]!["reason"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_files_are_written_and_omit_the_lease_id()
    {
        var fake = new FakeInvoker { HistoryPayload = """{"run_id":"r11","violations":[{"rule":"E1"}]}""" };

        _ = await RunAsync([Spec("M11")], fake);

        string json = await File.ReadAllTextAsync(Path.Combine(_folder, MutationRunJob.JsonFileName));
        string markdown = await File.ReadAllTextAsync(Path.Combine(_folder, MutationRunJob.MarkdownFileName));
        Assert.Contains("\"M11\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Lease, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Lease, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_without_a_pattern_detects_any_signature()
    {
        Result<string> verdict = MutationRunJob.Classify(["inv:I4|nodes|x"], null);

        Assert.True(verdict.IsSuccess);
        Assert.Equal("detected", verdict.Value);
    }

    [Fact]
    public void Classify_with_no_signature_survives()
    {
        Result<string> verdict = MutationRunJob.Classify([], Pattern);

        Assert.True(verdict.IsSuccess);
        Assert.Equal("survived", verdict.Value);
    }

    [Fact]
    public void Classify_with_only_other_signatures_is_detected_other()
    {
        Result<string> verdict = MutationRunJob.Classify(["hist:enrollment|E9|enroll-race"], Pattern);

        Assert.True(verdict.IsSuccess);
        Assert.Equal("detected_other", verdict.Value);
    }

    [Fact]
    public void Classify_with_an_invalid_pattern_fails()
    {
        Result<string> verdict = MutationRunJob.Classify(["x"], "[");

        Assert.True(verdict.IsFailure);
    }

    [Fact]
    public void Signatures_refuses_a_count_without_texts()
    {
        var detector = new MutantDetector("uat_run", null, null, "^inv:I4");

        Result<IReadOnlyList<string>> found = MutationRunJob.Signatures(detector, Payload("""{"ingest":{"signatures":2}}"""));

        Assert.True(found.IsFailure);
    }

    [Fact]
    public void Signatures_takes_listed_texts_as_written()
    {
        var detector = new MutantDetector("uat_run", null, null, "^inv:I4");

        Result<IReadOnlyList<string>> found = MutationRunJob.Signatures(detector, Payload("""{"signatures":["inv:I4|nodes|x"]}"""));

        Assert.True(found.IsSuccess);
        Assert.Single(found.Value!);
        Assert.Equal("inv:I4|nodes|x", found.Value![0]);
    }

    [Fact]
    public void Signatures_builds_history_texts_from_violations()
    {
        MutantDetector detector = HistoryDetector(Pattern);

        Result<IReadOnlyList<string>> found = MutationRunJob.Signatures(detector, Payload("""{"violations":[{"rule":"E1"}]}"""));

        Assert.True(found.IsSuccess);
        Assert.Single(found.Value!);
        Assert.Matches(Pattern, found.Value![0]);
    }

    [Fact]
    public async Task Detected_when_invariants_check_reports_the_expected_inv_signature()
    {
        var fake = new FakeInvoker
        {
            HistoryPayload = """{"run_id":"r12","violations":[]}""",
            InvariantsPayload = """{"count":1,"violations":[{"invariant_id":"I1","service":"nodes","check_id":"unhandled"}],"errors":[],"skipped":[]}""",
        };

        JobOutcome outcome = await RunAsync([SpecWith("M12", "^inv:I1\\|nodes\\|")], fake);

        Assert.Equal("detected", Verdict(outcome, "M12"));
        Assert.Equal(1, fake.Count("invariants_check"));
        Assert.Contains("inv:I1|nodes|unhandled", Mutant(outcome, "M12")["signatures"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invariants_check_error_is_unreachable()
    {
        var fake = new FakeInvoker
        {
            HistoryPayload = """{"run_id":"r13","violations":[{"rule":"E1"}]}""",
            InvariantsError = true,
        };

        JobOutcome outcome = await RunAsync([Spec("M13")], fake);

        Assert.Equal("unreachable", Verdict(outcome, "M13"));
        Assert.StartsWith("invariants_check failed: ", Mutant(outcome, "M13")["reason"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(1, Starts(fake, PlainBuild));
    }

    [Fact]
    public async Task Detector_error_text_reaches_the_reason()
    {
        const string secret = "d2-refused-token-4417";
        _vault.Add(SecretKind.EnrollmentToken, secret);
        var fake = new FakeInvoker
        {
            HistoryError = true,
            HistoryPayload = "\"history refused for " + secret + " | pipe\"",
        };

        JobOutcome outcome = await RunAsync([Spec("M14")], fake);

        string reason = Mutant(outcome, "M14")["reason"]!.GetValue<string>();
        Assert.Equal("unreachable", Verdict(outcome, "M14"));
        Assert.StartsWith("history_run failed: ", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, reason, StringComparison.Ordinal);
        string markdown = await File.ReadAllTextAsync(Path.Combine(_folder, MutationRunJob.MarkdownFileName));
        Assert.DoesNotContain(secret, markdown, StringComparison.Ordinal);
        Assert.Contains("\\| pipe", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Signatures_reads_ingest_signature_texts()
    {
        var detector = new MutantDetector("uat_run", null, null, "^inv:I4");

        Result<IReadOnlyList<string>> found = MutationRunJob.Signatures(
            detector,
            Payload("""{"ingest":{"ok":true,"signatures":1,"signature_texts":["inv:I4|nodes|x"]}}"""));

        Assert.True(found.IsSuccess, found.Error);
        Assert.Single(found.Value!);
        Assert.Equal("inv:I4|nodes|x", found.Value![0]);
    }

    [Fact]
    public async Task Signatures_reads_a_real_ResultsService_payload()
    {
        // The seam with wave5-fix-A: the ingest payload of the real service carries the I4 text that M5's pattern matches.
        string root = Path.Combine(_folder, "lab-root");
        string uatRuns = Path.Combine(root, "uat-runs");
        Directory.CreateDirectory(uatRuns);
        string bundle = BundleFixtures.Write(uatRuns, "run-d2", "b1", 42,
            new RowSpec("invariants", "I4", "FAIL", Violation: "I4", CheckId: "unhandled"));
        using var vault = new SecretVault();
        using var results = new ResultsService(
            LabPaths.FromRoot(root, repoRoot: ""),
            new PathValidator(NullLogger<PathValidator>.Instance),
            new Redactor(vault),
            new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance,
            _time);

        Result<IngestResult> ingested = await results.IngestAsync(bundle, CancellationToken.None);

        Assert.True(ingested.IsSuccess, ingested.Error);
        var payload = new JsonObject { ["ingest"] = results.ToPayload(ingested) };
        var detector = new MutantDetector("uat_run", null, null, "^inv:I4\\|nodes\\|");

        Result<IReadOnlyList<string>> found = MutationRunJob.Signatures(detector, JsonSerializer.SerializeToElement(payload));

        Assert.True(found.IsSuccess, found.Error);
        string text = found.Value!.Single(t => t.StartsWith("inv:I4|nodes|", StringComparison.Ordinal));
        Assert.Matches("^inv:I4\\|nodes\\|", text);
    }

    [Fact]
    public async Task Reports_redact_a_vault_value_in_the_title_and_escape_pipes()
    {
        // The title is not written to the reports, so the vault value rides in a signature (JSON and the Markdown cell) and the
        // pipes are checked in that cell.
        const string secret = "d2-report-secret-9023";
        _vault.Add(SecretKind.Password, secret);
        var fake = new FakeInvoker { HistoryPayload = $$"""{"run_id":"r15","signatures":["inv:I4|nodes|{{secret}}"]}""" };

        JobOutcome outcome = await RunAsync([SpecWith("M15", "^inv:I4\\|nodes\\|")], fake);

        Assert.Equal("detected", Verdict(outcome, "M15"));
        string json = await File.ReadAllTextAsync(Path.Combine(_folder, MutationRunJob.JsonFileName));
        string markdown = await File.ReadAllTextAsync(Path.Combine(_folder, MutationRunJob.MarkdownFileName));
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, markdown, StringComparison.Ordinal);
        Assert.Contains("inv:I4\\|nodes\\|", markdown, StringComparison.Ordinal);
    }

    private async Task<JobOutcome> RunAsync(
        IReadOnlyList<MutantSpec> mutants,
        FakeInvoker fake,
        Func<MutantSpec, CancellationToken, Task<Result<MutantBuildDto>>>? build = null,
        string? plain = PlainBuild,
        CancellationToken ct = default,
        RunningMutantBuild? running = null)
    {
        Directory.CreateDirectory(_folder);
        Func<MutantSpec, CancellationToken, Task<Result<MutantBuildDto>>> builder = build ?? BuildOk;
        var job = new MutationRunJob(
            mutants,
            builder,
            () => plain,
            fake.InvokeAsync,
            new GatewayPacer(_time),
            new PathValidator(NullLogger<PathValidator>.Instance),
            _time,
            new Redactor(_vault),
            running ?? new RunningMutantBuild());

        Result<JobOutcome> result = await job.RunAsync("job-test", Lease, _folder, ct);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private Task<Result<MutantBuildDto>> BuildOk(MutantSpec spec, CancellationToken ct) =>
        Task.FromResult(Result<MutantBuildDto>.Success(
            new MutantBuildDto(spec.Id, MutantBuilder.BuiltVerdict, null, "build-" + spec.Id, new string('b', 40), [], [], _time.GetUtcNow())));

    /// <summary>A mutant build whose id is the stamp form <c>mutant-&lt;id&gt;-&lt;commit8&gt;</c> (D-ML155).</summary>
    private static Func<MutantSpec, CancellationToken, Task<Result<MutantBuildDto>>> MutantBuildWithStamp() =>
        (spec, _) => Task.FromResult(Result<MutantBuildDto>.Success(
            new MutantBuildDto(spec.Id, MutantBuilder.BuiltVerdict, null, "mutant-" + spec.Id + "-bbbbbbbb", new string('b', 40), [], [], DateTimeOffset.UnixEpoch)));

    private static MutantSpec Spec(string id) =>
        new(id, MutantKind.Product, new string('a', 40), "Test mutant " + id, ["src/Nodes/Enrollment.cs"], ["nodes"], HistoryDetector(Pattern), id);

    private static MutantSpec SpecWith(string id, string pattern) =>
        new(id, MutantKind.Product, new string('a', 40), "Test mutant " + id, ["src/Nodes/Enrollment.cs"], ["nodes"], HistoryDetector(pattern), id);

    private static MutantDetector HistoryDetector(string? pattern) =>
        new("history_run", JsonNode.Parse("""{"scenario":"enroll-race"}""")!.AsObject(), null, pattern);

    private static JsonElement Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonObject Mutant(JobOutcome outcome, string id) =>
        outcome.Result!["mutants"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(entry => entry["id"]!.GetValue<string>() == id);

    private static string Verdict(JobOutcome outcome, string id) => Mutant(outcome, id)["verdict"]!.GetValue<string>();

    private static int Starts(FakeInvoker fake, string buildId) =>
        fake.Calls.Count(call => call.Tool == "svc_start" && call.Args["build_id"]?.GetValue<string>() == buildId);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        _vault.Dispose();
    }

    private sealed record Call(string Tool, JsonObject Args);

    private sealed class FakeInvoker
    {
        public List<Call> Calls { get; } = [];

        public string HistoryPayload { get; init; } = """{"run_id":"r0","violations":[]}""";

        public bool HistoryError { get; init; }

        public string? FailStartBuild { get; init; }

        public Action<string>? OnCall { get; init; }

        public string InvariantsPayload { get; init; } = """{"count":0,"violations":[],"errors":[],"skipped":[]}""";

        public bool InvariantsError { get; init; }

        public int Count(string tool) => Calls.Count(call => call.Tool == tool);

        public Task<LabToolResult> InvokeAsync(string tool, JsonElement args, LabCallContext context, CancellationToken ct)
        {
            Calls.Add(new Call(tool, JsonNode.Parse(args.GetRawText())!.AsObject()));
            OnCall?.Invoke(tool);
            ct.ThrowIfCancellationRequested();

            LabToolResult result = tool switch
            {
                "history_run" => new LabToolResult(HistoryError, Payload(HistoryPayload)),
                "invariants_check" => new LabToolResult(InvariantsError, Payload(InvariantsPayload)),
                "svc_start" => new LabToolResult(
                    Calls[^1].Args["build_id"]?.GetValue<string>() == FailStartBuild,
                    Payload("{}")),
                _ => new LabToolResult(true, Payload("{}")),
            };
            return Task.FromResult(result);
        }
    }
}
