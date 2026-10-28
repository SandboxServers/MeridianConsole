using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Triage;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>Arguments of the tools that take one bucket id.</summary>
public sealed record TriageBucketArgs(string? BucketId = null);

/// <summary>Arguments of <c>triage_mark</c>. The contract names the argument <c>issue_url</c>; it is validated as text.</summary>
[SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "The tool contract (C4) names a string argument issue_url.")]
public sealed record TriageMarkArgs(string? BucketId = null, string? State = null, string? IssueUrl = null);

/// <summary>Arguments of <c>results_known_issue_set</c>.</summary>
public sealed record KnownIssueSetArgs(string? BucketId = null, string? KnownIssueId = null);

/// <summary>
/// <c>triage_show</c> (Open): one bucket with its signature, hits, runs, known issue, repro and draft. Read-only.
/// </summary>
public sealed class TriageShowTool : LabTool<TriageBucketArgs>
{
    private readonly ResultsService _results;

    /// <summary>Creates the tool.</summary>
    public TriageShowTool(ResultsService results)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <inheritdoc />
    public override string Name => "triage_show";

    /// <inheritdoc />
    public override string Description =>
        "Shows one triage bucket: {bucket, signature, hits, first_seen, last_seen, runs, known_issue, repro_path, draft_path}.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(TriageBucketArgs args, LabCallContext context, CancellationToken ct)
    {
        if (!DraftWriter.IsValidBucketId(args.BucketId))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] bucket_id is not a valid bucket id");
        }

        string id = args.BucketId!;
        Result<object> read = await _results.ReadAsync<object>(db =>
        {
            Result<ResultsDb.BucketRecord> bucket = db.BucketOf(id);
            Result<string> text = db.SignatureTextOf(id);
            Result<IReadOnlyList<string>> runs = db.RunIdsOfSignature(id);
            if (bucket.IsFailure || text.IsFailure || runs.IsFailure)
            {
                return Result<object>.Failure(bucket.IsFailure ? bucket.Error : text.IsFailure ? text.Error : runs.Error);
            }

            ResultsDb.BucketRecord b = bucket.Value;
            return Result<object>.Success(new
            {
                bucket = b.SignatureId,
                signature = text.Value,
                hits = b.Hits,
                first_seen = b.FirstSeen,
                last_seen = b.LastSeen,
                runs = runs.Value,
                known_issue = b.KnownIssueId,
                repro_path = b.ReproPath,
                draft_path = b.DraftPath,
            });
        }, ct).ConfigureAwait(false);
        return read;
    }
}

/// <summary>
/// <c>triage_draft_issue</c> (Leased): writes the issue draft of one bucket under the triage folder. Never files it.
/// </summary>
public sealed class TriageDraftIssueTool : LabTool<TriageBucketArgs>
{
    private readonly DraftWriter _writer;

    /// <summary>Creates the tool.</summary>
    public TriageDraftIssueTool(DraftWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public override string Name => "triage_draft_issue";

    /// <inheritdoc />
    public override string Description =>
        "Writes the issue draft of one bucket under the triage folder and returns {draft_path}. Nothing is filed.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(TriageBucketArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<string> draft = await _writer.WriteAsync(args.BucketId ?? string.Empty, ct).ConfigureAwait(false);
        return draft.IsFailure
            ? Result<object>.Failure(draft.Error)
            : Result<object>.Success(new { draft_path = draft.Value });
    }
}

/// <summary>
/// <c>triage_mark</c> (Leased): sets a bucket's triage state and, optionally, the issue URL it was filed as. The URL must be an
/// issue of the project repository.
/// </summary>
public sealed partial class TriageMarkTool : LabTool<TriageMarkArgs>
{
    private static readonly string[] States = ["new", "known", "wontfix", "filed"];

    private readonly ResultsService _results;

    /// <summary>Creates the tool.</summary>
    public TriageMarkTool(ResultsService results)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <inheritdoc />
    public override string Name => "triage_mark";

    /// <inheritdoc />
    public override string Description =>
        "Sets a bucket's state (new, known, wontfix, filed) and optional issue_url (a SandboxServers/MeridianConsole issue). Returns {bucket_id, state}.";

    /// <summary>True when <paramref name="candidate"/> is an issue URL of the project repository.</summary>
    public static bool IsIssueUrl(string? candidate) => candidate is not null && IssueUrlPattern().IsMatch(candidate);

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(TriageMarkArgs args, LabCallContext context, CancellationToken ct)
    {
        if (!DraftWriter.IsValidBucketId(args.BucketId))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] bucket_id is not a valid bucket id");
        }

        if (args.State is null || !States.Contains(args.State, StringComparer.Ordinal))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] state must be one of " + string.Join(", ", States));
        }

        if (args.IssueUrl is not null && !IsIssueUrl(args.IssueUrl))
        {
            return Result<object>.Failure(
                "[Lab.InvalidArgument] issue_url must be https://github.com/SandboxServers/MeridianConsole/issues/<n>");
        }

        string id = args.BucketId!;
        string state = args.State;
        string? url = args.IssueUrl;
        Result<object> done = await _results.ReadAsync<object>(db =>
        {
            Result<ResultsDb.BucketRecord> bucket = db.BucketOf(id);
            if (bucket.IsFailure)
            {
                return Result<object>.Failure(bucket.Error);
            }

            Result set = db.SetSignatureTriage(id, state, url);
            return set.IsFailure
                ? Result<object>.Failure(set.Error)
                : Result<object>.Success(new { bucket_id = id, state });
        }, ct).ConfigureAwait(false);
        return done;
    }

    [GeneratedRegex(@"^https://github\.com/SandboxServers/MeridianConsole/issues/\d+\z", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex IssueUrlPattern();
}

/// <summary>
/// <c>results_known_issue_set</c> (Leased): links a bucket to an entry of the shipped known-issue file. The id must exist there.
/// </summary>
public sealed class ResultsKnownIssueSetTool : LabTool<KnownIssueSetArgs>
{
    private readonly ResultsService _results;

    /// <summary>Creates the tool.</summary>
    public ResultsKnownIssueSetTool(ResultsService results)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <inheritdoc />
    public override string Name => "results_known_issue_set";

    /// <inheritdoc />
    public override string Description =>
        "Links a bucket to a known issue id that exists in known-issues.toml. Returns {bucket_id, known_issue_id}.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(KnownIssueSetArgs args, LabCallContext context, CancellationToken ct)
    {
        if (!DraftWriter.IsValidBucketId(args.BucketId) || string.IsNullOrWhiteSpace(args.KnownIssueId))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] bucket_id and known_issue_id are required");
        }

        KnownIssueCheck check = _results.LoadKnownIssues();
        if (!check.Checked)
        {
            return Result<object>.Failure("[Lab.Unavailable] the shipped known-issue file is not available");
        }

        string issueId = args.KnownIssueId;
        if (!check.Issues.Issues.Any(i => string.Equals(i.Id, issueId, StringComparison.Ordinal)))
        {
            return Result<object>.Failure("[Lab.NotFound] known_issue_id is not in known-issues.toml");
        }

        string id = args.BucketId!;
        Result<object> done = await _results.ReadAsync<object>(db =>
        {
            Result<ResultsDb.BucketRecord> bucket = db.BucketOf(id);
            if (bucket.IsFailure)
            {
                return Result<object>.Failure(bucket.Error);
            }

            Result set = db.SetSignatureKnownIssue(id, issueId);
            return set.IsFailure
                ? Result<object>.Failure(set.Error)
                : Result<object>.Success(new { bucket_id = id, known_issue_id = issueId });
        }, ct).ConfigureAwait(false);
        return done;
    }
}
