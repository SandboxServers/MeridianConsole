using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Results;

/// <summary>A row's failure signatures and its claimed known issue, if any (C12 Matching).</summary>
/// <param name="Section">The spec section of the row (or of a standalone violation).</param>
/// <param name="RowId">The row id (or the empty string for a standalone violation).</param>
/// <param name="ClaimedKnownIssue">The row's <c>known_issue</c> claim, or null. A claim is never a pass.</param>
/// <param name="Signatures">Every failure signature of the row.</param>
public sealed record RowFailure(string Section, string RowId, string? ClaimedKnownIssue, IReadOnlyList<FailureSignature> Signatures);

/// <summary>
/// The known-issue verdict of a row. <paramref name="State"/> is <c>known</c>, <c>new</c>, <c>claimed_mismatch</c> or
/// <c>regressed</c>; <paramref name="IssueId"/> is the matched or claimed issue, and <paramref name="Reason"/> explains a
/// verdict that is not a plain match.
/// </summary>
public sealed record KnownMatch(string? IssueId, string State, string? Reason);

/// <summary>A compiled issue pattern: the source text as written and its regex (1 s timeout, culture invariant).</summary>
public sealed record KnownIssuePattern(string Source, Regex Regex, string Kind);

/// <summary>The scope of an issue (C12). An empty list does not narrow.</summary>
public sealed record KnownIssueScope(IReadOnlyList<string> Sections, IReadOnlyList<string> Rows, IReadOnlyList<string> Services,
    IReadOnlyList<string> Invariants);

/// <summary>One known issue of <c>docs/guides/uat-specs/known-issues.toml</c>.</summary>
public sealed record KnownIssue(string Id, string Title, string Link, IReadOnlyList<string> Signatures,
    IReadOnlyList<KnownIssuePattern> Patterns, string State, KnownIssueScope Scope);

/// <summary>
/// The shipped known issues (C12). <see cref="Load"/> lists every problem and never throws for bad input; <see cref="Match"/>
/// applies C12's Matching rules to one row.
/// </summary>
public sealed class KnownIssues
{
    /// <summary>The schema version this build reads.</summary>
    public const int SupportedSchema = 1;

    private const string OpenState = "open";
    private const string FixedState = "fixed";
    private const string StateNew = "new";
    private const string StateKnown = "known";
    private const string StateClaimedMismatch = "claimed_mismatch";
    private const string StateRegressed = "regressed";
    private const string PatternTimeoutLog = "Known issue {IssueId} pattern timed out; counted as no match";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "schema", "issue" };
    private static readonly HashSet<string> IssueKeys = new(StringComparer.Ordinal)
    {
        "id", "title", "link", "signatures", "patterns", "state", "scope",
    };
    private static readonly HashSet<string> ScopeKeys = new(StringComparer.Ordinal) { "sections", "rows", "services", "invariants" };

    private readonly ILogger? _logger;

    private KnownIssues(IReadOnlyList<KnownIssue> issues, ILogger? logger)
    {
        Issues = issues;
        _logger = logger;
    }

    /// <summary>The issues in file order.</summary>
    public IReadOnlyList<KnownIssue> Issues { get; }

    /// <summary>An empty set: nothing is linked to a known issue.</summary>
    public static KnownIssues Empty => new([], null);

    /// <summary>
    /// Loads the file at <paramref name="path"/>. An absent file is an empty set. Every problem found is listed in the failure.
    /// </summary>
    public static Result<KnownIssues> Load(string path, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<KnownIssues>.Failure("[Lab.InvalidArgument] the known-issues path is required");
        }

        if (!File.Exists(path))
        {
            return Result<KnownIssues>.Success(new KnownIssues([], logger));
        }

        try
        {
            string text = File.ReadAllText(path);
            return LoadText(Path.GetFileName(path), text, logger);
        }
        catch (IOException)
        {
            return Result<KnownIssues>.Failure("[Lab.Io] cannot read the known-issues file");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<KnownIssues>.Failure("[Lab.Io] cannot read the known-issues file: access denied");
        }
    }

    /// <summary>Parses known-issue text. <paramref name="file"/> is the name used in the problems.</summary>
    public static Result<KnownIssues> LoadText(string file, string text, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(text);

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            return Result<KnownIssues>.Failure("[Lab.InvalidArgument] " + file + ": invalid TOML: " + ex.Message);
        }

        var problems = new List<string>();
        foreach (string key in root.Keys)
        {
            if (!RootKeys.Contains(key))
            {
                problems.Add("unknown key " + key);
            }
        }

        if (!root.TryGetValue("schema", out object? schemaRaw) || schemaRaw is not long schema || schema != SupportedSchema)
        {
            problems.Add("schema must be " + SupportedSchema);
        }

        var issues = new List<KnownIssue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetValue("issue", out object? raw))
        {
            if (raw is not TomlTableArray array)
            {
                problems.Add("issue must be an array of tables");
            }
            else
            {
                for (int i = 0; i < array.Count; i++)
                {
                    KnownIssue? issue = ParseIssue(array[i], i, problems);
                    if (issue is null)
                    {
                        continue;
                    }

                    if (!ids.Add(issue.Id))
                    {
                        problems.Add("issue " + issue.Id + ": duplicate id");
                        continue;
                    }

                    issues.Add(issue);
                }
            }
        }

        if (problems.Count > 0)
        {
            return Result<KnownIssues>.Failure(
                "[Lab.InvalidArgument] " + file + " is invalid: " + string.Join("; ", problems));
        }

        return Result<KnownIssues>.Success(new KnownIssues(issues, logger));
    }

    /// <summary>
    /// The verdict for one row (C12 Matching). A row is known only when every signature matches the same open issue within its
    /// scope. A matching fixed issue makes the row new with state <c>regressed</c>. A claim that does not match is
    /// <c>claimed_mismatch</c>. A standalone violation is matched as a row of its own with one signature.
    /// </summary>
    public KnownMatch Match(RowFailure row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Signatures.Count == 0)
        {
            return row.ClaimedKnownIssue is null
                ? new KnownMatch(null, StateNew, "no failure signature")
                : new KnownMatch(row.ClaimedKnownIssue, StateClaimedMismatch,
                    "known_issue " + row.ClaimedKnownIssue + " claimed but the row has no failure signature");
        }

        if (row.ClaimedKnownIssue is not null)
        {
            // A claim is never a pass: only the claimed issue can link the row, and only when it matches every signature.
            KnownIssue? claimed = Issues.FirstOrDefault(i => i.Id == row.ClaimedKnownIssue);
            if (claimed is not null && MatchesAll(claimed, row))
            {
                return Verdict(claimed);
            }

            FailureSignature unmatched = claimed is null
                ? row.Signatures[0]
                : row.Signatures.FirstOrDefault(s => !Matches(claimed, row, s)) ?? row.Signatures[0];
            return new KnownMatch(row.ClaimedKnownIssue, StateClaimedMismatch,
                "known_issue " + row.ClaimedKnownIssue + " claimed but signature " + unmatched.Text + " does not match");
        }

        KnownIssue? matched = Issues.FirstOrDefault(i => i.State == OpenState && MatchesAll(i, row))
            ?? Issues.FirstOrDefault(i => i.State == FixedState && MatchesAll(i, row));
        return matched is null ? new KnownMatch(null, StateNew, null) : Verdict(matched);
    }

    private static KnownMatch Verdict(KnownIssue issue) =>
        issue.State == OpenState
            ? new KnownMatch(issue.Id, StateKnown, null)
            : new KnownMatch(issue.Id, StateRegressed, "regressed:" + issue.Id);

    private bool MatchesAll(KnownIssue issue, RowFailure row)
    {
        foreach (FailureSignature signature in row.Signatures)
        {
            if (!Matches(issue, row, signature))
            {
                return false;
            }
        }

        return true;
    }

    private bool Matches(KnownIssue issue, RowFailure row, FailureSignature signature)
    {
        if (!InScope(issue.Scope, row, signature))
        {
            return false;
        }

        if (issue.Signatures.Contains(signature.Text))
        {
            return true;
        }

        foreach (KnownIssuePattern pattern in issue.Patterns)
        {
            try
            {
                if (pattern.Regex.IsMatch(signature.Text))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _logger?.LogWarning(PatternTimeoutLog, issue.Id);
            }
        }

        return false;
    }

    private static bool InScope(KnownIssueScope scope, RowFailure row, FailureSignature signature)
    {
        if (scope.Sections.Count > 0 && !scope.Sections.Contains(row.Section))
        {
            return false;
        }

        if (scope.Rows.Count > 0 && !scope.Rows.Contains(row.RowId))
        {
            return false;
        }

        if (scope.Services.Count > 0 && (signature.Service is null || !scope.Services.Contains(signature.Service)))
        {
            return false;
        }

        return scope.Invariants.Count == 0
            || (signature.InvariantId is not null && scope.Invariants.Contains(signature.InvariantId));
    }

    private static KnownIssue? ParseIssue(TomlTable table, int index, List<string> problems)
    {
        string label = table.TryGetValue("id", out object? idRaw) && idRaw is string idText && idText.Length > 0
            ? "issue " + idText
            : "issue #" + (index + 1);
        int before = problems.Count;

        foreach (string key in table.Keys)
        {
            if (!IssueKeys.Contains(key))
            {
                problems.Add(label + ": unknown key " + key);
            }
        }

        string? id = StringValue(table, "id", label, problems);
        string title = StringValue(table, "title", label, problems) ?? string.Empty;
        string link = StringValue(table, "link", label, problems) ?? string.Empty;
        string? state = StringValue(table, "state", label, problems);
        if (state is not (OpenState or FixedState))
        {
            problems.Add(label + ": state must be open or fixed");
        }

        List<string> signatures = StringArray(table, "signatures", label, problems);
        foreach (string signature in signatures)
        {
            if (!SignatureBuilder.Kinds.Any(k => signature.StartsWith(k + ":", StringComparison.Ordinal)))
            {
                problems.Add(label + ": signature " + signature + " has no known kind prefix");
            }
        }

        var patterns = new List<KnownIssuePattern>();
        foreach (string source in StringArray(table, "patterns", label, problems))
        {
            KnownIssuePattern? pattern = CompilePattern(source, label, problems);
            if (pattern is not null)
            {
                patterns.Add(pattern);
            }
        }

        if (signatures.Count == 0 && patterns.Count == 0)
        {
            problems.Add(label + ": needs signatures or patterns");
        }

        KnownIssueScope scope = ParseScope(table, label, problems);
        ValidateScopeKinds(scope, signatures, patterns, label, problems);
        if (problems.Count > before || id is null)
        {
            return null;
        }

        return new KnownIssue(id, title, link, signatures, patterns, state!, scope);
    }

    private static void ValidateScopeKinds(KnownIssueScope scope, List<string> signatures, List<KnownIssuePattern> patterns,
        string label, List<string> problems)
    {
        if (scope.Services.Count == 0 && scope.Invariants.Count == 0)
        {
            return;
        }

        var kinds = new List<string>();
        foreach (string signature in signatures)
        {
            string? kind = SignatureBuilder.Kinds.FirstOrDefault(k => signature.StartsWith(k + ":", StringComparison.Ordinal));
            if (kind is not null)
            {
                kinds.Add(kind);
            }
        }

        kinds.AddRange(patterns.Select(p => p.Kind));
        foreach (string kind in kinds.Distinct(StringComparer.Ordinal))
        {
            if (scope.Services.Count > 0 && kind is not ("exc" or "http" or "inv"))
            {
                problems.Add(label + ": scope services cannot match a " + kind + " signature (it carries no service)");
            }

            if (scope.Invariants.Count > 0 && kind != "inv")
            {
                problems.Add(label + ": scope invariants cannot match a " + kind + " signature");
            }
        }
    }

    private static KnownIssuePattern? CompilePattern(string source, string label, List<string> problems)
    {
        string? kind = SignatureBuilder.Kinds.FirstOrDefault(k => source.StartsWith("^" + k + ":", StringComparison.Ordinal));
        if (kind is null)
        {
            problems.Add(label + ": pattern " + source + " must start with ^ and a kind prefix");
            return null;
        }

        string body = source[(kind.Length + 2)..];
        if (body.Trim() is ".*" or ".+")
        {
            problems.Add(label + ": pattern " + source + " is too broad");
            return null;
        }

        try
        {
            var bodyRegex = new Regex(body, RegexOptions.CultureInvariant, RegexTimeout);
            if (bodyRegex.IsMatch(string.Empty))
            {
                problems.Add(label + ": pattern " + source + " matches an empty string");
                return null;
            }

            var regex = new Regex(source, RegexOptions.CultureInvariant, RegexTimeout);
            return new KnownIssuePattern(source, regex, kind);
        }
        catch (ArgumentException)
        {
            problems.Add(label + ": pattern " + source + " is not a valid regular expression");
            return null;
        }
    }

    private static KnownIssueScope ParseScope(TomlTable table, string label, List<string> problems)
    {
        if (!table.TryGetValue("scope", out object? raw))
        {
            return new KnownIssueScope([], [], [], []);
        }

        if (raw is not TomlTable scope)
        {
            problems.Add(label + ": scope must be a table");
            return new KnownIssueScope([], [], [], []);
        }

        foreach (string key in scope.Keys)
        {
            if (!ScopeKeys.Contains(key))
            {
                problems.Add(label + ": scope has unknown key " + key);
            }
        }

        return new KnownIssueScope(
            StringArray(scope, "sections", label, problems),
            StringArray(scope, "rows", label, problems),
            StringArray(scope, "services", label, problems),
            StringArray(scope, "invariants", label, problems));
    }

    private static string? StringValue(TomlTable table, string key, string label, List<string> problems)
    {
        if (!table.TryGetValue(key, out object? raw))
        {
            if (key == "id")
            {
                problems.Add(label + ": id is required");
            }

            return null;
        }

        if (raw is string text && (key != "id" || text.Length > 0))
        {
            return text;
        }

        problems.Add(label + ": " + key + " must be a non-empty string");
        return null;
    }

    private static List<string> StringArray(TomlTable table, string key, string label, List<string> problems)
    {
        var values = new List<string>();
        if (!table.TryGetValue(key, out object? raw))
        {
            return values;
        }

        if (raw is not TomlArray array)
        {
            problems.Add(label + ": " + key + " must be an array of strings");
            return values;
        }

        foreach (object? item in array)
        {
            if (item is string text && text.Length > 0)
            {
                values.Add(text);
            }
            else
            {
                problems.Add(label + ": " + key + " holds a value that is not a non-empty string");
            }
        }

        return values;
    }
}
