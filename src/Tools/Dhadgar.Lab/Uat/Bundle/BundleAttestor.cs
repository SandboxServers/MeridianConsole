using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// Who made an attest call: the owner of the lease it carried and the caller that sent it (D-ML67).
/// </summary>
/// <param name="LeaseOwner">The owner of the lease.</param>
/// <param name="Caller">The caller, for example <c>mcp</c>.</param>
public sealed record AttestedBy(string LeaseOwner, string Caller);

/// <summary>
/// Records a human verdict on one pending human clause (C8). It rewrites the row JSON, regrades the row with
/// <see cref="RowGrader"/>, rewrites <c>ledger.md</c> from every row file, and recomputes <c>overall</c> in <c>run.json</c>.
/// Only a <c>NEEDS_HUMAN</c> or <c>UNVERIFIED</c> row is attested. Every other grade (FAIL, BLOCKED, SKIPPED, PASS), non-human clauses
/// and runs whose secret scan failed are refused. Each attestation records the lease owner and the caller (D-ML67).
/// </summary>
public static class BundleAttestor
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly System.Text.RegularExpressions.Regex IdPattern =
        new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Attests one human clause and returns the row's new grade text.
    /// </summary>
    /// <param name="folder">The validated bundle folder.</param>
    /// <param name="runId">The run id.</param>
    /// <param name="section">The section id of the row.</param>
    /// <param name="row">The row id.</param>
    /// <param name="clauseId">The clause id.</param>
    /// <param name="verdict">Either <c>held</c> or <c>not_held</c>. <c>pass</c> and <c>fail</c> are refused.</param>
    /// <param name="note">An optional note of at most 1000 characters with no control, format, line or paragraph separator, private-use or unassigned code points (<see cref="LedgerRenderer.IsUnsafeText"/>), redacted before it is written.</param>
    /// <param name="at">The time of the attestation.</param>
    /// <param name="by">The lease owner and the caller of the attest call.</param>
    /// <param name="redactor">Redacts every written text.</param>
    /// <param name="validator">Validates every path.</param>
    public static Result<string> Attest(string folder, string runId, string section, string row, string clauseId, string verdict,
        string? note, DateTimeOffset at, AttestedBy by, Redactor redactor, IPathValidator validator)
    {
        if (verdict is not ("held" or "not_held"))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] verdict must be held or not_held");
        }

        if (!IdPattern.IsMatch(section ?? string.Empty) || !IdPattern.IsMatch(row ?? string.Empty) || !IdPattern.IsMatch(clauseId ?? string.Empty))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] section, row or clause id is not valid");
        }

        if (note is not null && (note.Length > 1000 || LedgerRenderer.IsUnsafeText(note)))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] note must be at most 1000 characters without control, format or separator characters");
        }

        try
        {
            Result<string> runFile = BundleFolders.ResolveFile(folder, "run.json", validator);
            if (runFile.IsFailure)
            {
                return Result<string>.Failure(runFile.Error);
            }

            if (JsonNode.Parse(File.ReadAllText(runFile.Value!)) is not JsonObject runObj)
            {
                return Result<string>.Failure("[Lab.Unavailable] run.json is not readable");
            }

            if (runObj["secret_scan"]?.GetValue<string>() == "failed")
            {
                return Result<string>.Failure("[Lab.InvalidArgument] the secret scan failed; the run cannot be attested");
            }

            Result<string> rowFile = BundleFolders.ResolveFile(folder, Path.Combine("rows", section!, row! + ".json"), validator);
            if (rowFile.IsFailure)
            {
                return Result<string>.Failure(rowFile.Error);
            }

            if (JsonNode.Parse(File.ReadAllText(rowFile.Value!)) is not JsonObject rowObj || rowObj["clauses"] is not JsonArray clauses)
            {
                return Result<string>.Failure("[Lab.Unavailable] the row file is not a bundle row");
            }

            string? storedGrade = rowObj["grade"]?.GetValue<string>();
            if (storedGrade is not ("NEEDS_HUMAN" or "UNVERIFIED"))
            {
                return Result<string>.Failure("[Lab.InvalidArgument] only a NEEDS_HUMAN or UNVERIFIED row is attested");
            }

            JsonObject? clause = clauses.OfType<JsonObject>().FirstOrDefault(candidate => candidate["id"]?.GetValue<string>() == clauseId);
            if (clause is null)
            {
                return Result<string>.Failure("[Lab.NotFound] no such clause in the row");
            }

            if (clause["source"]?.GetValue<string>() != "human")
            {
                return Result<string>.Failure("[Lab.InvalidArgument] only human clauses are attested");
            }

            if (clause["state"]?.GetValue<string>() != nameof(ClauseState.Pending))
            {
                return Result<string>.Failure("[Lab.InvalidArgument] the clause is not pending");
            }

            clause["state"] = verdict == "held" ? nameof(ClauseState.Held) : nameof(ClauseState.NotHeld);
            if (rowObj["attestations"] is not JsonArray attestations)
            {
                attestations = new JsonArray();
                rowObj["attestations"] = attestations;
            }

            attestations.Add(new JsonObject
            {
                ["clause_id"] = clauseId,
                ["verdict"] = verdict,
                ["note"] = note ?? string.Empty,
                ["at"] = at.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["lease_owner"] = by.LeaseOwner,
                ["caller"] = by.Caller,
            });

            var inputs = clauses.OfType<JsonObject>()
                .Select(candidate => (Required: candidate["required"]?.GetValue<bool>() ?? true, State: ParseState(candidate["state"]?.GetValue<string>())))
                .ToList();
            (RowGrade grade, string reason) = RowGrader.Grade(ran: true, skipReason: null, stepError: null, inputs);
            rowObj["grade"] = LedgerRenderer.ResultText(grade);
            rowObj["reason"] = reason;

            Result written = WriteRedacted(folder, Path.Combine("rows", section!, row! + ".json"), rowObj.ToJsonString(JsonOptions), redactor, validator);
            if (written.IsFailure)
            {
                return Result<string>.Failure(written.Error);
            }

            List<LedgerRow> rows = ReadRows(folder, redactor);
            string system = runObj["os"]?.GetValue<string>() ?? string.Empty;
            written = WriteRedacted(folder, "ledger.md", LedgerRenderer.Render(runId, system, BuildStamp.FromRunJson(runObj).LedgerLine, rows), redactor, validator);
            if (written.IsFailure)
            {
                return Result<string>.Failure(written.Error);
            }

            RecomputeRun(runObj, rows);
            written = WriteRedacted(folder, "run.json", runObj.ToJsonString(JsonOptions), redactor, validator);
            if (written.IsFailure)
            {
                return Result<string>.Failure(written.Error);
            }

            return Result<string>.Success(LedgerRenderer.ResultText(grade));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return Result<string>.Failure("[Lab.Unavailable] the attestation could not be written");
        }
    }

    private static ClauseState ParseState(string? text) =>
        Enum.TryParse(text, ignoreCase: false, out ClauseState state) ? state : ClauseState.Unverified;

    private static RowGrade ParseGrade(string? text) => text switch
    {
        "PASS" => RowGrade.Pass,
        "FAIL" => RowGrade.Fail,
        "BLOCKED" => RowGrade.Blocked,
        "SKIPPED" => RowGrade.Skipped,
        "NEEDS_HUMAN" => RowGrade.NeedsHuman,
        _ => RowGrade.Unverified,
    };

    private static List<LedgerRow> ReadRows(string folder, Redactor redactor)
    {
        var rows = new List<LedgerRow>();
        string rowsRoot = Path.Combine(folder, "rows");
        if (!Directory.Exists(rowsRoot))
        {
            return rows;
        }

        // Free text is redacted before the renderer rewrites it (see BundleWriter.RowFinished).
        string Scrub(string text) => redactor.Redact(text);
        string? ScrubOrNull(string? text) => text is null ? null : redactor.Redact(text);

        foreach (string file in Directory.EnumerateFiles(rowsRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject obj)
            {
                continue;
            }

            var clauses = (obj["clauses"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(clause => new LedgerClause(
                clause["id"]?.GetValue<string>() ?? string.Empty,
                Scrub(clause["pointer"]?.GetValue<string>() ?? string.Empty),
                ParseState(clause["state"]?.GetValue<string>()),
                ScrubOrNull(clause["error"]?.GetValue<string>()),
                clause["source"]?.GetValue<string>() ?? string.Empty,
                clause["required"]?.GetValue<bool>() ?? true)).ToList();
            var attestations = (obj["attestations"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(attestation => new LedgerAttestation(
                attestation["clause_id"]?.GetValue<string>() ?? string.Empty,
                attestation["verdict"]?.GetValue<string>() ?? string.Empty,
                Scrub(attestation["note"]?.GetValue<string>() ?? string.Empty),
                attestation["at"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UnixEpoch,
                attestation["lease_owner"]?.GetValue<string>() ?? string.Empty,
                attestation["caller"]?.GetValue<string>() ?? string.Empty)).ToList();
            rows.Add(new LedgerRow(
                obj["section"]?.GetValue<string>() ?? string.Empty,
                obj["id"]?.GetValue<string>() ?? string.Empty,
                Scrub(obj["title"]?.GetValue<string>() ?? string.Empty),
                Scrub(obj["expected"]?.GetValue<string>() ?? string.Empty),
                ParseGrade(obj["grade"]?.GetValue<string>()),
                obj["at"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UnixEpoch,
                clauses,
                attestations));
        }

        return rows
            .OrderBy(row => row.Section, StringComparer.Ordinal)
            .ThenBy(row => row.Row, StringComparer.Ordinal)
            .ToList();
    }

    private static void RecomputeRun(JsonObject runObj, IReadOnlyList<LedgerRow> rows)
    {
        var summary = new JsonObject();
        foreach (string key in new[] { "PASS", "FAIL", "BLOCKED", "SKIPPED", "NEEDS_HUMAN", "UNVERIFIED" })
        {
            summary[key] = rows.Count(row => LedgerRenderer.ResultText(row.Grade) == key);
        }

        string[] failed = rows.Where(row => row.Grade is RowGrade.Fail or RowGrade.Blocked or RowGrade.Unverified)
            .Select(row => row.Section + "/" + row.Row).ToArray();
        RunOverall overall = RunOutcome.Compute(rows.Select(row => row.Grade).ToList(),
            failed.Select(key => (FailureKey: key, IsKnown: false)).ToList());

        runObj["summary"] = summary;
        runObj["new_failures"] = new JsonArray(failed.Select(key => (JsonNode)JsonValue.Create(key)!).ToArray());
        runObj["overall"] = overall switch
        {
            RunOverall.Pass => "pass",
            RunOverall.PassWithKnown => "pass_with_known",
            RunOverall.Incomplete => "incomplete",
            _ => "fail",
        };
        runObj["fidelity"] = "evidence_only";
    }

    private static Result WriteRedacted(string folder, string relative, string text, Redactor redactor, IPathValidator validator)
    {
        Result<string> path = validator.ValidatePath(Path.Combine(folder, relative), [folder]);
        if (path.IsFailure)
        {
            return Result.Failure("[Lab.InvalidArgument] the bundle path is outside the bundle folder");
        }

        File.WriteAllText(path.Value!, redactor.Redact(text), new UTF8Encoding(false));
        return Result.Success();
    }
}
