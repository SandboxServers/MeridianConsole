using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Results;

/// <summary>
/// What a flake check reruns, read from the source run's bundle before the job starts. <c>Keys</c> are the selected
/// rows as <c>section/row</c>; <c>SourceGrades</c> holds the grade of every row of the source run under the same key.
/// </summary>
public sealed record FlakeCheckPlan(
    string SourceRunId,
    string SpecFile,
    ulong Seed,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> Rows,
    IReadOnlyList<string> Keys,
    IReadOnlyDictionary<string, string> SourceGrades,
    int Reruns);

/// <summary>
/// The flake check (<c>results_flake_check</c>, C4 and C12): reruns the named rows of one run with the same seed and the
/// current build, through <c>uat_run</c>, <c>reruns</c> times. Each rerun is ingested by <c>uat_run</c>, and the ingest
/// marks a row whose grade differs across runs of one build and seed as a flake (<see cref="BundleIngestor"/>). The job
/// outcome is <c>fail</c> when any row is flaky or any selected row does not pass in every run; it is <c>pass</c> only when
/// every run grades every selected row <c>PASS</c>. A flake is never a pass.
/// </summary>
public static class FlakeCheckJob
{
    /// <summary>The job kind, also the bundle's <c>runs.kind</c>.</summary>
    public const string Kind = "flake_check";

    /// <summary>The default number of reruns.</summary>
    public const int DefaultReruns = 2;

    /// <summary>The largest number of reruns one job may run.</summary>
    public const int MaxReruns = 5;

    private const string RowsFolder = "rows";
    private const string RunFile = "run.json";

    /// <summary>
    /// Reads the source run's <c>run.json</c> and rows and selects the rows to rerun: the named <paramref name="rowIds"/>,
    /// or every <c>FAIL</c> row when none are named. A named row that the run does not have is refused.
    /// </summary>
    /// <param name="uatRuns">The bundle root (<c>LabPaths.UatRuns</c>).</param>
    /// <param name="validator">The path validator.</param>
    /// <param name="runId">The source run id.</param>
    /// <param name="rowIds">The row ids to rerun, or null for the failed rows.</param>
    /// <param name="reruns">The number of reruns, 1 to <see cref="MaxReruns"/>.</param>
    /// <param name="ct">The cancellation token.</param>
    public static async Task<Result<FlakeCheckPlan>> PlanAsync(string uatRuns, IPathValidator validator, string runId,
        IReadOnlyList<string>? rowIds, int reruns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (reruns < 1 || reruns > MaxReruns)
        {
            return Result<FlakeCheckPlan>.Failure("[Lab.InvalidArgument] reruns must be 1 to 5");
        }

        Result<string> folder = BundleFolders.Find(uatRuns, runId, validator);
        if (folder.IsFailure)
        {
            return Result<FlakeCheckPlan>.Failure(folder.Error);
        }

        Result<JsonObject> run = await ReadRunAsync(folder.Value!, validator, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<FlakeCheckPlan>.Failure(run.Error);
        }

        string? specFile = SpecFileOf(run.Value!);
        if (specFile is null || BundleSeed.ReadSeed(run.Value!) is not { } seed)
        {
            return Result<FlakeCheckPlan>.Failure("[Lab.InvalidArgument] the run has no spec or seed to rerun");
        }

        Result<IReadOnlyList<RowGradeEntry>> entries = await ReadRowGradesAsync(folder.Value!, validator, ct).ConfigureAwait(false);
        if (entries.IsFailure)
        {
            return Result<FlakeCheckPlan>.Failure(entries.Error);
        }

        var sourceGrades = entries.Value!.ToDictionary(entry => entry.Key, entry => entry.Grade, StringComparer.Ordinal);
        IReadOnlyList<RowGradeEntry> selected;
        if (rowIds is { Count: > 0 })
        {
            // A row is named by its id, or by its section/row key (the form the job result lists). A bare id that
            // occurs in more than one section is ambiguous, so it is refused rather than guessed.
            foreach (string rowId in rowIds)
            {
                bool byKey = rowId.Contains('/');
                List<RowGradeEntry> hits = entries.Value!.Where(entry => byKey ? entry.Key == rowId : entry.RowId == rowId).ToList();
                if (hits.Count == 0)
                {
                    return Result<FlakeCheckPlan>.Failure("[Lab.NotFound] row " + rowId + " is not in the run");
                }

                if (!byKey && hits.Select(entry => entry.Section).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    return Result<FlakeCheckPlan>.Failure("[Lab.InvalidArgument] row " + rowId + " is ambiguous; name it as section/row");
                }
            }

            selected = entries.Value!
                .Where(entry => rowIds.Any(rowId => rowId.Contains('/') ? entry.Key == rowId : entry.RowId == rowId))
                .ToList();
        }
        else
        {
            selected = entries.Value!.Where(entry => entry.Grade == "FAIL").ToList();
        }

        if (selected.Count == 0)
        {
            return Result<FlakeCheckPlan>.Failure("[Lab.InvalidArgument] the run has no failed rows to rerun");
        }

        var plan = new FlakeCheckPlan(
            runId,
            specFile,
            seed,
            selected.Select(entry => entry.Section).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            selected.Select(entry => entry.RowId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            selected.Select(entry => entry.Key).Order(StringComparer.Ordinal).ToList(),
            sourceGrades,
            reruns);
        return Result<FlakeCheckPlan>.Success(plan);
    }

    /// <summary>
    /// Closes the plan's rows over the spec's <c>depends_on</c> (F115), so each rerun's <c>uat_run</c> receives a filter that
    /// passes the check <c>uat_run</c> applies. <see cref="FlakeCheckPlan.Keys"/> stays the selected rows, so only those rows
    /// are judged for flakes. Refused when the closed filter still fails <see cref="RowFilterCheck.Check"/>.
    /// </summary>
    /// <param name="plan">The plan from <see cref="PlanAsync"/>.</param>
    /// <param name="spec">The spec the plan names, loaded by the caller.</param>
    public static Result<FlakeCheckPlan> WithSpec(FlakeCheckPlan plan, UatSpec spec)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(spec);

        IReadOnlyList<string> closed = RowFilterCheck.Close(spec, plan.Rows);
        Result check = RowFilterCheck.Check(spec, closed);
        if (check.IsFailure)
        {
            return Result<FlakeCheckPlan>.Failure(check.Error);
        }

        return Result<FlakeCheckPlan>.Success(plan with { Rows = closed });
    }

    /// <summary>
    /// The job work: runs <c>uat_run</c> through the dispatcher with the job's own lease for each rerun, reads each rerun's
    /// rows, and returns the grades per row across the source run and the reruns. The job's lease id goes only to the
    /// dispatcher's <c>lease_id</c> argument; it is never logged or written to the outcome.
    /// </summary>
    /// <param name="plan">The plan from <see cref="PlanAsync"/>.</param>
    /// <param name="context">The job context.</param>
    /// <param name="invoke">The tool call: the dispatcher's <c>InvokeAsync</c> in the daemon, a fake in tests.</param>
    /// <param name="uatRuns">The bundle root.</param>
    /// <param name="validator">The path validator.</param>
    /// <param name="ct">The job's cancellation token.</param>
    public static async Task<Result<JobOutcome>> RunAsync(FlakeCheckPlan plan, JobContext context,
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke,
        string uatRuns, IPathValidator validator, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);

        var grades = new List<IReadOnlyDictionary<string, string>> { plan.SourceGrades };
        var runs = new JsonArray();
        for (int i = 0; i < plan.Reruns; i++)
        {
            ct.ThrowIfCancellationRequested();
            context.Report("rerun " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + plan.Reruns.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var arguments = JsonSerializer.SerializeToElement(new
            {
                spec = plan.SpecFile,
                sections = plan.Sections,
                rows = plan.Rows,
                seed = plan.Seed,
                lease_id = context.LeaseId,
            });
            var callContext = new LabCallContext(Guid.NewGuid().ToString("N"), context.LeaseId, "job:" + context.JobId);
            LabToolResult call = await invoke("uat_run", arguments, callContext, ct).ConfigureAwait(false);
            if (call.IsError)
            {
                return Result<JobOutcome>.Failure("[Lab.RerunFailed] rerun " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " did not complete");
            }

            string? rerunId = StringOf(call.Payload, "run_id");
            if (rerunId is null)
            {
                return Result<JobOutcome>.Failure("[Lab.RerunFailed] rerun " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " returned no run id");
            }

            Result<string> folder = BundleFolders.Find(uatRuns, rerunId, validator);
            if (folder.IsFailure)
            {
                return Result<JobOutcome>.Failure(folder.Error);
            }

            Result<IReadOnlyList<RowGradeEntry>> rerunRows = await ReadRowGradesAsync(folder.Value!, validator, ct).ConfigureAwait(false);
            if (rerunRows.IsFailure)
            {
                return Result<JobOutcome>.Failure(rerunRows.Error);
            }

            grades.Add(rerunRows.Value!.ToDictionary(entry => entry.Key, entry => entry.Grade, StringComparer.Ordinal));
            runs.Add(new JsonObject { ["run_id"] = rerunId, ["overall"] = StringOf(call.Payload, "overall") });
        }

        var keys = plan.Keys.Order(StringComparer.Ordinal).ToList();
        var flakes = new JsonArray();
        foreach (string key in keys)
        {
            var values = grades.Select(g => g.TryGetValue(key, out string? grade) ? grade : "missing").ToList();
            if (values.Distinct(StringComparer.Ordinal).Count() > 1)
            {
                flakes.Add(new JsonObject
                {
                    ["row"] = key,
                    ["grades"] = new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
                });
            }
        }

        bool allPass = plan.Keys.All(key => grades.All(g => g.TryGetValue(key, out string? grade) && grade == "PASS"));
        string overall = flakes.Count == 0 && allPass ? "pass" : "fail";
        var result = new JsonObject
        {
            ["source_run_id"] = plan.SourceRunId,
            ["seed"] = plan.Seed,
            ["reruns"] = plan.Reruns,
            ["rows"] = new JsonArray(plan.Keys.Select(key => (JsonNode)JsonValue.Create(key)!).ToArray()),
            ["runs"] = runs,
            ["flakes"] = flakes,
            ["state"] = flakes.Count > 0 ? "flaky" : "stable",
        };
        return Result<JobOutcome>.Success(new JobOutcome(overall, result));
    }

    private static async Task<Result<JsonObject>> ReadRunAsync(string folder, IPathValidator validator, CancellationToken ct)
    {
        Result<string> file = BundleFolders.ResolveFile(folder, RunFile, validator);
        if (file.IsFailure)
        {
            return Result<JsonObject>.Failure(file.Error);
        }

        Result<string> text = await BundleFolders.ReadTextAsync(file.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<JsonObject>.Failure(text.Error);
        }

        return JsonNode.Parse(text.Value!) is JsonObject run
            ? Result<JsonObject>.Success(run)
            : Result<JsonObject>.Failure("[Lab.InvalidArgument] run.json is not an object");
    }

    private static string? SpecFileOf(JsonObject run) =>
        run["specs"] is JsonArray { Count: > 0 } specs && specs[0] is JsonObject first ? Text(first, "file") : null;

    /// <summary>Reads every row file of a bundle as <c>section/row</c> and grade.</summary>
    private static async Task<Result<IReadOnlyList<RowGradeEntry>>> ReadRowGradesAsync(string folder, IPathValidator validator,
        CancellationToken ct)
    {
        var entries = new List<RowGradeEntry>();
        string rowsRoot = Path.Combine(folder, RowsFolder);
        if (!Directory.Exists(rowsRoot))
        {
            return Result<IReadOnlyList<RowGradeEntry>>.Success(entries);
        }

        foreach (string file in Directory.EnumerateFiles(rowsRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            Result<string> path = validator.ValidatePath(file, [folder]);
            if (path.IsFailure)
            {
                return Result<IReadOnlyList<RowGradeEntry>>.Failure("[Lab.InvalidArgument] a row file is outside the bundle folder");
            }

            Result<string> text = await BundleFolders.ReadTextAsync(path.Value!, ct).ConfigureAwait(false);
            if (text.IsFailure)
            {
                return Result<IReadOnlyList<RowGradeEntry>>.Failure(text.Error);
            }

            if (JsonNode.Parse(text.Value!) is not JsonObject row || Text(row, "section") is not { } section
                || Text(row, "id") is not { } rowId || Text(row, "grade") is not { } grade)
            {
                return Result<IReadOnlyList<RowGradeEntry>>.Failure("[Lab.InvalidArgument] a row file has no section, id or grade");
            }

            entries.Add(new RowGradeEntry(section, rowId, grade));
        }

        return Result<IReadOnlyList<RowGradeEntry>>.Success(entries);
    }

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string? StringOf(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>One row of a bundle: its section, id and grade text.</summary>
    private sealed record RowGradeEntry(string Section, string RowId, string Grade)
    {
        public string Key => Section + "/" + RowId;
    }
}
