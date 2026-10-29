using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Uat;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Results;

/// <summary>
/// The verdict of one graded row (C12 Matching): its scrubbed failure signatures, their ids, the bucketing primary and the
/// known-issue match. <see cref="KnownMatchText"/> is the value stored in <c>rows.known_match</c>. A passing row has no signature
/// and no match.
/// </summary>
/// <param name="Signatures">The scrubbed failure signatures of the row, in C12 order.</param>
/// <param name="SignatureIds">The distinct signature ids of the row, in the same order.</param>
/// <param name="PrimaryId">The id of the primary signature, or null.</param>
/// <param name="Match">The known-issue verdict, or null for a row that does not fail.</param>
/// <param name="KnownMatchText">The stored <c>known_match</c>: the issue id, <c>claimed_mismatch</c>, <c>regressed:&lt;id&gt;</c>, or null.</param>
public sealed record RowVerdict(IReadOnlyList<FailureSignature> Signatures, IReadOnlyList<string> SignatureIds, string? PrimaryId,
    KnownMatch? Match, string? KnownMatchText);

/// <summary>A standalone violation of a row, as read from a row file.</summary>
internal sealed record ParsedViolation(ViolationEvidence Evidence, string? Detail, string? At);

/// <summary>
/// One step of a row file. <paramref name="ExpectMet"/> is set only when the step declared an expected error (<c>expect_met</c>).
/// </summary>
internal sealed record ParsedStep(string Id, bool Ok, bool? ExpectMet);

/// <summary>One fault the row injected, as the row file's <c>faults</c> entry (L25f, C12 <c>faults</c>).</summary>
internal sealed record ParsedFault(string FaultId, string Kind, string TargetId, string? InjectedAt, string? HealedAt, string? HealError);

/// <summary>A graded row as read from a row file: the grade, the reason, its clauses, violations and steps.
/// <paramref name="Exceptions"/> and <paramref name="Http"/> are the row's <c>evidence</c> (L25e), empty when absent.
/// <paramref name="Faults"/> are the row's injected faults (L25f), empty when absent. <paramref name="History"/> holds a history
/// bundle's violations (L25f), empty for every other row.</summary>
internal sealed record ParsedRow(string Section, string RowId, RowGrade Grade, string? Reason, string? StartedAt,
    string? EndedAt, string? KnownIssue, IReadOnlyList<ClauseEvidence> Clauses, IReadOnlyList<ParsedViolation> Violations,
    IReadOnlyList<ParsedStep> Steps, IReadOnlyList<ExceptionEvidence>? Exceptions = null, IReadOnlyList<HttpEvidence>? Http = null,
    IReadOnlyList<ParsedFault>? Faults = null, IReadOnlyList<HistoryViolationEvidence>? History = null);

/// <summary>
/// The one row-verdict path (L26 B1, D-ML92). The ingest, the run writer and the attestor all call <see cref="Of"/>, and
/// <see cref="Parse"/> reads a row file, so no caller keeps its own copy of the matching rules.
/// </summary>
internal static class RowVerdicts
{
    /// <summary>Builds the verdict of <paramref name="row"/> against <paramref name="known"/>. Every stored text passes through <paramref name="scrub"/>.</summary>
    public static RowVerdict Of(ParsedRow row, KnownIssues known, Func<string, string> scrub)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(scrub);

        if (row.Grade is not (RowGrade.Fail or RowGrade.Blocked or RowGrade.Unverified))
        {
            return new RowVerdict([], [], null, null, null);
        }

        var evidence = new RowEvidence(row.Section, row.RowId, row.Exceptions ?? [], row.Http ?? [],
            row.Clauses.Concat(StepClauses(row)).ToList(),
            row.Violations.Select(v => v.Evidence).ToList(), row.History ?? [], []);
        List<FailureSignature> scrubbed = SignatureBuilder.BuildAll(evidence)
            .Select(signature => signature with { Text = scrub(signature.Text) })
            .ToList();

        var ids = new List<string>();
        foreach (FailureSignature signature in scrubbed)
        {
            string id = SignatureBuilder.SignatureId(signature.Text);
            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        FailureSignature? primary = SignatureBuilder.Primary(evidence) is { } first
            ? first with { Text = scrub(first.Text) }
            : null;
        string? primaryId = primary is null ? null : SignatureBuilder.SignatureId(primary.Text);

        KnownMatch match = known.Match(new RowFailure(row.Section, row.RowId, row.KnownIssue, scrubbed));
        string? knownMatchText = match.State switch
        {
            "known" => match.IssueId,
            "claimed_mismatch" => "claimed_mismatch",
            "regressed" => "regressed:" + match.IssueId,
            _ => null,
        };

        return new RowVerdict(scrubbed, ids, primaryId, match, knownMatchText);
    }

    /// <summary>
    /// The step pseudo-clauses of a row (L26 B3, C12 <c>clause</c>): a step whose expected error was not met is
    /// <c>step:&lt;id&gt;|NotHeld</c>, and so is the step that errored and stopped the row (one with no expected error).
    /// </summary>
    private static IEnumerable<ClauseEvidence> StepClauses(ParsedRow row)
    {
        foreach (ParsedStep step in row.Steps)
        {
            bool notHeld = step.ExpectMet is { } met ? !met : !step.Ok;
            if (notHeld)
            {
                yield return new ClauseEvidence(row.Section, row.RowId, "step:" + step.Id, "NotHeld");
            }
        }
    }

    /// <summary>Parses one row file. Fails, never throws, for a row with no section or id.</summary>
    public static Result<ParsedRow> Parse(JsonObject obj)
    {
        string? section = BundleIngestor.Text(obj, "section");
        string? rowId = BundleIngestor.Text(obj, "id");
        if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(rowId))
        {
            return Result<ParsedRow>.Failure("[Lab.InvalidArgument] a row file has no section or id");
        }

        var clauses = BundleIngestor.Objects(obj, "clauses")
            .Where(clause => BundleIngestor.Bool(clause, "required") ?? true)
            .Select(clause => new ClauseEvidence(section, rowId, BundleIngestor.Text(clause, "id") ?? string.Empty,
                BundleIngestor.Text(clause, "state") ?? string.Empty))
            .ToList();
        var violations = BundleIngestor.Objects(obj, "violations")
            .Select(v => new ParsedViolation(
                new ViolationEvidence(BundleIngestor.Text(v, "invariant_id") ?? string.Empty,
                    BundleIngestor.Text(v, "service") ?? string.Empty,
                    BundleIngestor.Text(v, "check_id") ?? string.Empty, BundleIngestor.Bool(v, "allowed") ?? false),
                BundleIngestor.Text(v, "detail"), BundleIngestor.Text(v, "at")))
            .ToList();

        var steps = BundleIngestor.Objects(obj, "steps")
            .Select(s => new ParsedStep(BundleIngestor.Text(s, "id") ?? string.Empty, BundleIngestor.Bool(s, "ok") ?? true,
                BundleIngestor.Bool(s, "expect_met")))
            .ToList();

        JsonObject? evidence = obj["evidence"] as JsonObject;
        var exceptions = (evidence is null ? [] : BundleIngestor.Objects(evidence, "exceptions"))
            .Select(e => new ExceptionEvidence(BundleIngestor.Text(e, "service") ?? string.Empty,
                BundleIngestor.Text(e, "type") ?? string.Empty, Texts(e, "frames")))
            .ToList();
        var http = (evidence is null ? [] : BundleIngestor.Objects(evidence, "http"))
            .Select(h =>
            {
                string route = BundleIngestor.Text(h, "route") ?? string.Empty;
                return new HttpEvidence(BundleIngestor.Text(h, "service") ?? string.Empty, IntOf(h, "status") ?? 0,
                    BundleIngestor.Text(h, "error_code"), BundleIngestor.Text(h, "method") ?? string.Empty, route, route,
                    Texts(h, "faults"));
            })
            .ToList();

        var faults = BundleIngestor.Objects(obj, "faults")
            .Select(f => new ParsedFault(BundleIngestor.Text(f, "fault_id") ?? string.Empty, BundleIngestor.Text(f, "kind") ?? string.Empty,
                BundleIngestor.Text(f, "target_id") ?? string.Empty, BundleIngestor.Text(f, "injected_at"),
                BundleIngestor.Text(f, "healed_at"), BundleIngestor.Text(f, "heal_error")))
            .ToList();

        return Result<ParsedRow>.Success(new ParsedRow(section, rowId, ParseGrade(BundleIngestor.Text(obj, "grade")),
            BundleIngestor.Text(obj, "reason"), BundleIngestor.Text(obj, "started_at"), BundleIngestor.Text(obj, "ended_at"),
            BundleIngestor.Text(obj, "known_issue"), clauses, violations, steps, exceptions, http, faults));
    }

    /// <summary>The string items of an array field; a non-string item is skipped.</summary>
    private static List<string> Texts(JsonObject obj, string key) =>
        obj[key] is JsonArray items
            ? items.Select(item => item is JsonValue value && value.TryGetValue(out string? text) ? text : null)
                .OfType<string>().ToList()
            : [];

    /// <summary>An integer field, or null when absent or not an integer.</summary>
    private static int? IntOf(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue(out int number) ? number : null;

    /// <summary>Maps a row grade text (C11) to its grade. Any other text is unverified.</summary>
    public static RowGrade ParseGrade(string? text) => text switch
    {
        "PASS" => RowGrade.Pass,
        "FAIL" => RowGrade.Fail,
        "BLOCKED" => RowGrade.Blocked,
        "SKIPPED" => RowGrade.Skipped,
        "NEEDS_HUMAN" => RowGrade.NeedsHuman,
        _ => RowGrade.Unverified,
    };
}
