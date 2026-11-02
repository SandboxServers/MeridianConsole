using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.History.Models;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Mutants;

/// <summary>
/// One mutant's verdict in a <c>mutation_run</c> (C19): <c>detected</c>, <c>detected_other</c>, <c>survived</c>, <c>stale</c> or
/// <c>unreachable</c>, with the detector runs, the signature texts they produced and the reason when there is one.
/// </summary>
/// <param name="Id">The mutant id.</param>
/// <param name="Verdict">The verdict text.</param>
/// <param name="DetectorRunIds">The run ids of the detector attempts that ran.</param>
/// <param name="Signatures">The signature texts the detector attempts reported.</param>
/// <param name="Reason">Why the verdict is not a plain detection; null otherwise.</param>
/// <param name="CheckErrors">The checks that could not run (the <c>errors</c> of each <c>invariants_check</c>), summed over the attempts.</param>
public sealed record MutantVerdictDto(string Id, string Verdict, IReadOnlyList<string> DetectorRunIds, IReadOnlyList<string> Signatures, string? Reason, int CheckErrors = 0);

/// <summary>
/// The <c>mutation</c> job (L54, C19, A1). For each product mutant: build it from HEAD with its patch, start its services on the
/// mutant build, run the detector through <see cref="LabToolDispatcher"/> under the job's lease, then judge the signatures.
/// The plain build is always restored in <c>finally</c>, under its own 5-minute budget that is never the job's token (A1), so a
/// cancelled or failed run does not leave a mutant running. Writes <c>mutants.json</c> and <c>mutants.md</c> into the job folder.
/// </summary>
public sealed class MutationRunJob
{
    /// <summary>The job kind.</summary>
    public const string Kind = "mutation";

    /// <summary>The restore budget (A1): a start takes longer than a heal, and the job's own token may already be cancelled.</summary>
    internal static readonly TimeSpan RestoreBudget = TimeSpan.FromMinutes(5);

    /// <summary>Attempts for a <c>schedule_approximate</c> detector (history runs); an exact detector gets one attempt.</summary>
    internal const int ScheduleApproximateAttempts = 5;

    /// <summary>The tool that starts a service on a build.</summary>
    internal const string ServiceStartTool = "svc_start";

    /// <summary>The detector tool that is paced on the gateway.</summary>
    internal const string PacedDetectorTool = "uat_run";

    /// <summary>The history detector tool, which runs schedule-approximate attempts.</summary>
    internal const string HistoryDetectorTool = "history_run";

    /// <summary>The longest detector or check error text kept in a verdict reason.</summary>
    internal const int MaxErrorText = 300;

    /// <summary>File name of the JSON result inside the job folder.</summary>
    public const string JsonFileName = "mutants.json";

    /// <summary>File name of the Markdown result inside the job folder.</summary>
    public const string MarkdownFileName = "mutants.md";

    private const string CallerPrefix = "job:mutation";

    private readonly IReadOnlyList<MutantSpec> _mutants;
    private readonly Func<MutantSpec, CancellationToken, Task<Result<MutantBuildDto>>> _build;
    private readonly Func<string?> _plainBuildId;
    private readonly Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> _invoke;
    private readonly GatewayPacer _pacer;
    private readonly IPathValidator _validator;
    private readonly TimeProvider _time;
    private readonly Redactor _redactor;
    private readonly RunningMutantBuild _running;

    /// <summary>
    /// Creates the job. Production passes <c>MutantBuilder.BuildAsync</c>, the dispatcher's <c>InvokeAsync</c> and the latest plain build;
    /// tests pass fakes.
    /// </summary>
    /// <param name="mutants">The product mutants to run, in order.</param>
    /// <param name="build">Builds one mutant; a stale patch is reported in the build's verdict.</param>
    /// <param name="plainBuildId">The plain build to restore, or null when the lab has none.</param>
    /// <param name="invoke">The tool call.</param>
    /// <param name="pacer">Paces the <c>uat_run</c> detector on the gateway.</param>
    /// <param name="validator">Validates the result files' paths.</param>
    /// <param name="time">The clock for the restore budget.</param>
    /// <param name="redactor">Redacts every string the job writes to its result files.</param>
    /// <param name="running">Records the mutant build while it serves the services (D-ML155).</param>
    public MutationRunJob(
        IReadOnlyList<MutantSpec> mutants,
        Func<MutantSpec, CancellationToken, Task<Result<MutantBuildDto>>> build,
        Func<string?> plainBuildId,
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke,
        GatewayPacer pacer,
        IPathValidator validator,
        TimeProvider time,
        Redactor redactor,
        RunningMutantBuild running)
    {
        _running = running ?? throw new ArgumentNullException(nameof(running));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _mutants = mutants ?? throw new ArgumentNullException(nameof(mutants));
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _plainBuildId = plainBuildId ?? throw new ArgumentNullException(nameof(plainBuildId));
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Runs every mutant, restores the plain build, and writes the result files. A cancelled run ends as <c>cancelled</c> after the
    /// restore; a failed restore is reported in the result (I5 then reports any mutant container left behind).
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="leaseId">The job's own lease; it is never logged or written to a file.</param>
    /// <param name="folder">The job folder that receives <c>mutants.json</c> and <c>mutants.md</c>.</param>
    /// <param name="ct">The job's token.</param>
    public async Task<Result<JobOutcome>> RunAsync(string jobId, string leaseId, string folder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(leaseId);
        ArgumentException.ThrowIfNullOrEmpty(folder);

        string? plain = _plainBuildId();
        var verdicts = new List<MutantVerdictDto>();
        var started = new HashSet<string>(StringComparer.Ordinal);
        string overall = "complete";
        Result restore;

        try
        {
            foreach (MutantSpec spec in _mutants)
            {
                if (ct.IsCancellationRequested)
                {
                    overall = "cancelled";
                    break;
                }

                verdicts.Add(await RunMutantAsync(spec, leaseId, started, ct).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            overall = "cancelled";
        }
        finally
        {
            restore = await RestorePlainAsync(plain, leaseId, started).ConfigureAwait(false);
            if (restore.IsSuccess)
            {
                _running.Clear();
            }
        }

        if (restore.IsFailure && overall == "complete")
        {
            overall = "restore_failed";
        }

        var result = new JsonObject
        {
            ["job_id"] = jobId,
            ["overall"] = overall,
            ["restore"] = new JsonObject
            {
                ["ok"] = restore.IsSuccess,
                ["build_id"] = plain,
                ["reason"] = restore.IsFailure ? _redactor.Redact(restore.Error) : null,
            },
            ["mutants"] = ToJson(verdicts),
        };

        Result written = await WriteReportsAsync(folder, result, verdicts).ConfigureAwait(false);
        if (written.IsFailure)
        {
            return Result<JobOutcome>.Failure(written.Error);
        }

        return Result<JobOutcome>.Success(new JobOutcome(overall, result));
    }

    /// <summary>Builds one mutant, starts its services and runs its detector. Any failure is an <c>unreachable</c> verdict.</summary>
    private async Task<MutantVerdictDto> RunMutantAsync(MutantSpec spec, string leaseId, HashSet<string> started, CancellationToken ct)
    {
        Result<MutantBuildDto> built = await _build(spec, ct).ConfigureAwait(false);
        if (built.IsFailure)
        {
            return Unreachable(spec.Id, "build failed: " + built.Error);
        }

        if (string.Equals(built.Value!.Verdict, MutantBuilder.StaleVerdict, StringComparison.Ordinal))
        {
            return new MutantVerdictDto(spec.Id, MutantBuilder.StaleVerdict, [], [], built.Value.Reason);
        }

        _running.Set(new MutantMark(built.Value.BuildId, spec.Id, built.Value.Commit));
        foreach (string service in spec.Services)
        {
            // Recorded before the call so a start that fails half way is still restored.
            _ = started.Add(service);
            var args = new JsonObject
            {
                ["service"] = service,
                ["build_id"] = built.Value.BuildId,
                [LabToolDispatcher.LeaseIdArgument] = leaseId,
            };
            LabToolResult call = await _invoke(ServiceStartTool, JsonSerializer.SerializeToElement(args), CallContext(leaseId), ct)
                .ConfigureAwait(false);
            if (call.IsError)
            {
                return Unreachable(spec.Id, "svc_start failed for " + service);
            }
        }

        return await RunDetectorAsync(spec, leaseId, ct).ConfigureAwait(false);
    }

    /// <summary>Runs the detector (one attempt, or up to five for a history run) and judges the signatures it reports.</summary>
    private async Task<MutantVerdictDto> RunDetectorAsync(MutantSpec spec, string leaseId, CancellationToken ct)
    {
        MutantDetector detector = spec.Detector;
        if (string.IsNullOrEmpty(detector.Tool))
        {
            return Unreachable(spec.Id, "the detector names no lab tool");
        }

        int attempts = string.Equals(detector.Tool, HistoryDetectorTool, StringComparison.Ordinal) ? ScheduleApproximateAttempts : 1;
        var runIds = new List<string>();
        var signatures = new List<string>();
        int checkErrors = 0;
        string verdict = "survived";
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (string.Equals(detector.Tool, PacedDetectorTool, StringComparison.Ordinal))
            {
                Result paced = await _pacer.WaitAsync(ct).ConfigureAwait(false);
                if (paced.IsFailure)
                {
                    return Unreachable(spec.Id, paced.Error, runIds);
                }
            }

            JsonObject args = detector.Args is null ? new JsonObject() : (JsonObject)detector.Args.DeepClone();
            args[LabToolDispatcher.LeaseIdArgument] = leaseId;
            DateTimeOffset attemptStart = _time.GetUtcNow();
            LabToolResult call = await _invoke(detector.Tool, JsonSerializer.SerializeToElement(args), CallContext(leaseId), ct)
                .ConfigureAwait(false);
            if (call.IsError)
            {
                return Unreachable(spec.Id, detector.Tool + " failed: " + ErrorText(call.Payload), runIds);
            }

            if (StringOf(call.Payload, "run_id") is { } runId)
            {
                runIds.Add(runId);
            }

            Result<IReadOnlyList<string>> found = Signatures(detector, call.Payload);
            if (found.IsFailure)
            {
                return Unreachable(spec.Id, found.Error, runIds);
            }

            // The invariants that broke in this attempt's window join its signatures (R3-N2 reader half, D-ML100).
            var attemptTexts = new List<string>(found.Value!);
            var checkArgs = new JsonObject { ["since"] = attemptStart.ToString("O", CultureInfo.InvariantCulture) };
            LabToolResult check = await _invoke("invariants_check", JsonSerializer.SerializeToElement(checkArgs), CallContext(leaseId), ct)
                .ConfigureAwait(false);
            if (check.IsError)
            {
                return Unreachable(spec.Id, "invariants_check failed: " + ErrorText(check.Payload), runIds);
            }

            attemptTexts.AddRange(InvariantTexts(check.Payload));
            checkErrors += ArrayLength(check.Payload, "errors");
            signatures.AddRange(attemptTexts);
            Result<string> classified = Classify(attemptTexts, detector.ExpectPattern);
            if (classified.IsFailure)
            {
                return Unreachable(spec.Id, classified.Error, runIds);
            }

            string judged = classified.Value!;
            if (judged == "detected")
            {
                verdict = "detected";
                break;
            }

            if (judged == "detected_other")
            {
                verdict = "detected_other";
            }
        }

        string? reason = verdict switch
        {
            "survived" => "no signature matched the expected pattern",
            "detected_other" => "a signature other than the expected one appeared",
            _ => null,
        };
        return new MutantVerdictDto(spec.Id, verdict, runIds, signatures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), reason, checkErrors);
    }

    /// <summary>The error text of a failed call: a string payload as written, anything else as its JSON. Redacted, then cut to 300 characters.</summary>
    private string ErrorText(JsonElement payload)
    {
        string text = payload.ValueKind == JsonValueKind.String ? payload.GetString() ?? string.Empty : payload.GetRawText();
        string redacted = _redactor.Redact(text);
        return redacted.Length > MaxErrorText ? redacted[..MaxErrorText] : redacted;
    }

    /// <summary>
    /// Restores the plain build on every service a mutant was started on. Runs under its own budget (A1), never the job's token.
    /// Returns a failure that names the services it could not restore.
    /// </summary>
    private async Task<Result> RestorePlainAsync(string? plain, string leaseId, HashSet<string> started)
    {
        if (started.Count == 0)
        {
            return Result.Success();
        }

        if (plain is null)
        {
            return Result.Failure("[Lab.NotFound] no plain build to restore; run lab_build first");
        }

        using var budget = new CancellationTokenSource(RestoreBudget, _time);
        var failed = new List<string>();
        foreach (string service in started.Order(StringComparer.Ordinal))
        {
            var args = new JsonObject
            {
                ["service"] = service,
                ["build_id"] = plain,
                [LabToolDispatcher.LeaseIdArgument] = leaseId,
            };
            try
            {
                LabToolResult call = await _invoke(ServiceStartTool, JsonSerializer.SerializeToElement(args), CallContext(leaseId), budget.Token)
                    .ConfigureAwait(false);
                if (call.IsError)
                {
                    failed.Add(service);
                }
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                failed.Add(service);
            }
        }

        return failed.Count == 0
            ? Result.Success()
            : Result.Failure("[Lab.Unavailable] plain build restore failed for: " + string.Join(", ", failed));
    }

    /// <summary>
    /// The signature texts of one detector result, in this order: the top-level <c>signatures</c> array as written; a history
    /// run's violations as <c>hist:</c> signatures; the ingest's <c>signature_texts</c> (R3-N2). An ingest that was not stored
    /// (<c>ok</c> false) is refused, and so is a count above 0 with no text. A detector that reports signatures without their texts
    /// is refused.
    /// </summary>
    internal static Result<IReadOnlyList<string>> Signatures(MutantDetector detector, JsonElement payload)
    {
        var texts = new List<string>();
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return Result<IReadOnlyList<string>>.Success(texts);
        }

        if (payload.TryGetProperty("signatures", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            AddTexts(texts, listed);
        }

        if (string.Equals(detector.Tool, HistoryDetectorTool, StringComparison.Ordinal)
            && payload.TryGetProperty("violations", out JsonElement violations)
            && violations.ValueKind == JsonValueKind.Array)
        {
            string scenario = StringOf(detector.Args, "scenario") ?? string.Empty;
            foreach (JsonElement violation in violations.EnumerateArray())
            {
                if (violation.ValueKind == JsonValueKind.Object
                    && violation.TryGetProperty("rule", out JsonElement rule)
                    && rule.ValueKind == JsonValueKind.String)
                {
                    texts.Add(SignatureBuilder.History(EnrollmentHistoryModel.ModelName, rule.GetString()!, scenario).Text);
                }
            }
        }

        if (!payload.TryGetProperty("ingest", out JsonElement ingest) || ingest.ValueKind != JsonValueKind.Object)
        {
            return Result<IReadOnlyList<string>>.Success(texts);
        }

        if (ingest.TryGetProperty("signature_texts", out JsonElement stored) && stored.ValueKind == JsonValueKind.Array)
        {
            AddTexts(texts, stored);
        }

        if (ingest.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.Unavailable] the detector's run was not ingested");
        }

        bool counted = ingest.TryGetProperty("signatures", out JsonElement count)
            && count.ValueKind == JsonValueKind.Number
            && count.TryGetInt64(out long value)
            && value > 0;
        return counted && texts.Count == 0
            ? Result<IReadOnlyList<string>>.Failure("[Lab.Unavailable] the detector reports signatures but not their text")
            : Result<IReadOnlyList<string>>.Success(texts);
    }

    /// <summary>Adds each string item of a JSON array; other items are skipped.</summary>
    private static void AddTexts(List<string> texts, JsonElement array)
    {
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                texts.Add(text);
            }
        }
    }

    /// <summary>The <c>inv:</c> signature texts of one <c>invariants_check</c> payload, one per <c>violations[]</c> entry.</summary>
    private static List<string> InvariantTexts(JsonElement payload)
    {
        var texts = new List<string>();
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("violations", out JsonElement violations)
            && violations.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement violation in violations.EnumerateArray())
            {
                string? invariantId = StringOf(violation, "invariant_id");
                string? service = StringOf(violation, "service");
                string? checkId = StringOf(violation, "check_id");
                if (invariantId is not null && service is not null && checkId is not null)
                {
                    texts.Add(SignatureBuilder.Invariant(invariantId, service, checkId).Text);
                }
            }
        }

        return texts;
    }

    /// <summary>The number of entries of a JSON array property; 0 when the property is absent or not an array.</summary>
    private static int ArrayLength(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    /// <summary>
    /// The verdict of one attempt: <c>detected</c> when a signature matches the expected pattern (any signature when no pattern is
    /// named), <c>detected_other</c> when only other signatures appeared, else <c>survived</c>.
    /// </summary>
    internal static Result<string> Classify(IReadOnlyList<string> signatures, string? expectPattern)
    {
        if (signatures.Count == 0)
        {
            return Result<string>.Success("survived");
        }

        if (expectPattern is null)
        {
            return Result<string>.Success("detected");
        }

        Regex expected;
        try
        {
            expected = new Regex(expectPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the mutant's expect_pattern is not a valid regular expression");
        }

        bool matched = false;
        bool other = false;
        foreach (string signature in signatures)
        {
            if (expected.IsMatch(signature))
            {
                matched = true;
            }
            else
            {
                other = true;
            }
        }

        return Result<string>.Success(matched ? "detected" : other ? "detected_other" : "survived");
    }

    private static LabCallContext CallContext(string leaseId) => new(Guid.NewGuid().ToString("N"), leaseId, CallerPrefix);

    private static MutantVerdictDto Unreachable(string id, string reason, List<string>? runIds = null) =>
        new(id, "unreachable", runIds ?? [], [], reason);

    private JsonArray ToJson(IEnumerable<MutantVerdictDto> verdicts)
    {
        var array = new JsonArray();
        foreach (MutantVerdictDto verdict in verdicts)
        {
            array.Add(new JsonObject
            {
                ["id"] = verdict.Id,
                ["verdict"] = verdict.Verdict,
                ["detector_run_ids"] = new JsonArray(verdict.DetectorRunIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
                ["signatures"] = new JsonArray(verdict.Signatures.Select(text => (JsonNode)JsonValue.Create(_redactor.Redact(text))!).ToArray()),
                ["reason"] = verdict.Reason is null ? null : _redactor.Redact(verdict.Reason),
                ["check_errors"] = verdict.CheckErrors,
            });
        }

        return array;
    }

    /// <summary>Writes <c>mutants.json</c> and <c>mutants.md</c> into the job folder, each path through the validator.</summary>
    private async Task<Result> WriteReportsAsync(string folder, JsonObject result, IReadOnlyList<MutantVerdictDto> verdicts)
    {
        Result<string> json = _validator.ValidatePath(Path.Combine(folder, JsonFileName), [folder]);
        Result<string> markdown = _validator.ValidatePath(Path.Combine(folder, MarkdownFileName), [folder]);
        if (json.IsFailure || markdown.IsFailure)
        {
            return Result.Failure("[Lab.InvalidArgument] the job folder is not valid");
        }

        try
        {
            await File.WriteAllTextAsync(json.Value!, result.ToJsonString(LabJson.Options)).ConfigureAwait(false);
            await File.WriteAllTextAsync(markdown.Value!, Markdown(result, verdicts)).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] the mutation report could not be written");
        }
    }

    private string Markdown(JsonObject result, IReadOnlyList<MutantVerdictDto> verdicts)
    {
        var text = new StringBuilder();
        text.Append("# Mutation run\n\n");
        text.Append("Overall: ").Append(result["overall"]?.ToString()).Append("\n\n");
        text.Append("| Mutant | Verdict | Detector runs | Signatures | Reason |\n");
        text.Append("| --- | --- | --- | --- | --- |\n");
        foreach (MutantVerdictDto verdict in verdicts)
        {
            text.Append("| ").Append(Cell(verdict.Id))
                .Append(" | ").Append(Cell(verdict.Verdict))
                .Append(" | ").Append(verdict.DetectorRunIds.Count)
                .Append(" | ").Append(Cell(string.Join(", ", verdict.Signatures)))
                .Append(" | ").Append(Cell(verdict.Reason))
                .Append(" |\n");
        }

        return text.ToString();
    }

    /// <summary>A Markdown table cell: redacted, with <c>|</c> and a backtick escaped and line breaks turned into spaces.</summary>
    private string Cell(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : _redactor.Redact(text)
                .Replace("|", "\\|", StringComparison.Ordinal)
                .Replace("`", "\\`", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

    private static string? StringOf(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string? StringOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
