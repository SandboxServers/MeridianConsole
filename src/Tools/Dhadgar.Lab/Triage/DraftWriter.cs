using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Triage;

/// <summary>
/// Writes an issue draft for one bucket under <c>triage/drafts</c> (L26, D-ML28, D-ML42). The draft is text for the owner; no code
/// here files it or calls GitHub. The text is scanned by <see cref="SecretScanner"/> before it is written, and a hit refuses the
/// draft.
/// </summary>
public sealed partial class DraftWriter
{
    /// <summary>The last line of every draft.</summary>
    public const string DraftOnlyLine = "Draft only. Filed by the owner after approval (D-ML28, D-ML42).";

    private const string DraftsFolder = "drafts";

    private readonly ResultsService _results;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly SecretScanner _scanner;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the writer.
    /// </summary>
    /// <param name="results">The results service that owns the index and its gate.</param>
    /// <param name="paths">The lab layout (the triage folder).</param>
    /// <param name="validator">Validates the draft path.</param>
    /// <param name="scanner">Scans the draft text for secrets before it is written.</param>
    /// <param name="time">The clock for the finding creation time.</param>
    public DraftWriter(ResultsService results, LabPaths paths, IPathValidator validator, SecretScanner scanner, TimeProvider time)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// True when <paramref name="bucketId"/> is safe to use as a file name and a database key.
    /// </summary>
    public static bool IsValidBucketId(string? bucketId) => bucketId is not null && BucketIdPattern().IsMatch(bucketId);

    /// <summary>
    /// Writes the draft of one bucket and records its path on the finding. Returns the draft path.
    /// </summary>
    /// <param name="bucketId">The bucket (signature) id.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<string>> WriteAsync(string bucketId, CancellationToken ct)
    {
        if (!IsValidBucketId(bucketId))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] bucket_id is not a valid bucket id");
        }

        string now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        Result<DraftSource> source = await _results.ReadAsync(db => Gather(db, bucketId, now), ct).ConfigureAwait(false);
        if (source.IsFailure)
        {
            return Result<string>.Failure(source.Error);
        }

        string text = Render(source.Value);
        if (_scanner.HasSecret(text))
        {
            return Result<string>.Failure("[Lab.Unavailable] the draft holds a secret value and was not written");
        }

        string folder = Path.Combine(_paths.Triage, DraftsFolder);
        Result<string> path = _validator.ValidatePath(Path.Combine(folder, bucketId + ".md"), [_paths.Triage]);
        if (path.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the draft path is outside the triage folder");
        }

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(path.Value, text, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure("[Lab.IoError] the draft could not be written: " + ex.GetType().Name);
        }

        string draftPath = path.Value;
        Result<bool> recorded = await _results.ReadAsync(db => Record(db, bucketId, draftPath), ct).ConfigureAwait(false);
        return recorded.IsFailure ? Result<string>.Failure(recorded.Error) : Result<string>.Success(draftPath);
    }

    /// <summary>
    /// Renders the draft text. Public so the golden test can compare it.
    /// </summary>
    internal static string Render(DraftSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var text = new StringBuilder();
        text.Append("# ").Append(Line(source.Bucket.Kind)).Append(" failure: ").Append(Line(source.SignatureText)).Append("\n\n");
        text.Append("- Bucket: ").Append(Line(source.Bucket.SignatureId)).Append('\n');
        text.Append("- Signature: ").Append(Line(source.SignatureText)).Append('\n');
        text.Append("- First seen: ").Append(Line(source.Bucket.FirstSeen)).Append(" (run ").Append(Line(source.FirstRun)).Append(")\n");
        text.Append("- Last seen: ").Append(Line(source.Bucket.LastSeen)).Append(" (run ").Append(Line(source.LastRun)).Append(")\n");
        text.Append("- Hits: ").Append(source.Bucket.Hits.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("- Build: ").Append(Line(source.BuildId ?? "unknown")).Append('\n');
        text.Append("- Seed: ").Append(source.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("- Repro: ").Append(Line(source.Bucket.ReproPath ?? "none")).Append("\n\n");
        text.Append("## Replay\n\n");
        text.Append("```text\n");
        text.Append(source.Bucket.ReproPath is null
            ? "no repro file was recorded for this bucket"
            : "meridian-lab uat run --replay " + Line(source.Bucket.ReproPath));
        text.Append("\n```\n\n");
        text.Append("## Excerpt (redacted)\n\n");
        text.Append("```text\n").Append(source.SignatureText.Replace("```", "'''", StringComparison.Ordinal)).Append("\n```\n\n");
        text.Append(DraftOnlyLine).Append('\n');
        return text.ToString();
    }

    private static string Line(string value) => value.ReplaceLineEndings(" ");

    private static Result<DraftSource> Gather(ResultsDb db, string bucketId, string now)
    {
        Result<ResultsDb.BucketRecord> bucket = db.BucketOf(bucketId);
        if (bucket.IsFailure)
        {
            return Result<DraftSource>.Failure(bucket.Error);
        }

        Result<string> signatureText = db.SignatureTextOf(bucketId);
        Result<IReadOnlyList<string>> runs = db.RunIdsOfSignature(bucketId);
        if (signatureText.IsFailure || runs.IsFailure)
        {
            return Result<DraftSource>.Failure(signatureText.IsFailure ? signatureText.Error : runs.Error);
        }

        string firstRun = runs.Value.Count > 0 ? runs.Value[0] : "none";
        string lastRun = runs.Value.Count > 0 ? runs.Value[^1] : "none";
        string? build = null;
        ulong seed = 0;
        if (runs.Value.Count > 0)
        {
            Result<RunRecord> run = db.RunOf(lastRun);
            if (run.IsSuccess)
            {
                build = run.Value.BuildId;
                seed = BundleSeed.FromStored(run.Value.Seed);
            }
        }

        // A bucket with no finding gets one, so the draft path has a row to live on.
        Result opened = db.InsertFinding(bucketId, runs.Value.Count > 0 ? runs.Value[0] : bucketId, null, now);
        if (opened.IsFailure)
        {
            return Result<DraftSource>.Failure(opened.Error);
        }

        return Result<DraftSource>.Success(new DraftSource(bucket.Value, signatureText.Value, firstRun, lastRun, build, seed));
    }

    private static Result<bool> Record(ResultsDb db, string bucketId, string draftPath)
    {
        Result set = db.SetFindingDraft(bucketId, draftPath);
        return set.IsFailure ? Result<bool>.Failure(set.Error) : Result<bool>.Success(true);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BucketIdPattern();

    /// <summary>The facts a draft is rendered from.</summary>
    internal sealed record DraftSource(ResultsDb.BucketRecord Bucket, string SignatureText, string FirstRun, string LastRun,
        string? BuildId, ulong Seed);
}
