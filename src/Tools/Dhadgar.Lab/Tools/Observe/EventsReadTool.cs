using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Observe;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Observe;

/// <summary>
/// <c>events_read</c> arguments (C4). Snake-case on the wire through <see cref="LabJson"/>.
/// </summary>
public sealed record EventsReadArgs(string RunId, long? AfterSeq = null, string? Actor = null, string? Kind = null, int? Max = null);

/// <summary>
/// The <c>events_read</c> result: <c>{events:[LabObservation], next_seq}</c>. <c>next_seq</c> is the last sequence read, to page with.
/// </summary>
public sealed record EventsReadResult(IReadOnlyList<LabObservation> Events, long NextSeq);

/// <summary>
/// Resolves a run id to its bundle or job folder through the run index (R-PATH). The index is the <c>run_id</c> in each
/// folder's <c>run.json</c>; the id is never used as a path.
/// </summary>
public static class RunIndex
{
    private static readonly Regex RunIdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The folder whose <c>run.json</c> names <paramref name="runId"/>, searched under each root. An invalid or unknown id is <c>[Lab.NotFound]</c>.
    /// </summary>
    /// <param name="roots">The roots to search, for example <c>LabPaths.UatRuns</c> and <c>LabPaths.Jobs</c>.</param>
    /// <param name="runId">The run id from the caller.</param>
    /// <param name="validator">Validates each folder against its root.</param>
    public static Result<string> Find(IEnumerable<string> roots, string? runId, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(validator);
        if (string.IsNullOrEmpty(runId) || !RunIdPattern.IsMatch(runId))
        {
            return Result<string>.Failure("[Lab.NotFound] no run with that run_id");
        }

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                Result<string> folder = validator.ValidatePath(dir, [root]);
                if (folder.IsSuccess && RunJsonNames(folder.Value, runId))
                {
                    return folder;
                }
            }
        }

        return Result<string>.Failure("[Lab.NotFound] no run with that run_id");
    }

    private static bool RunJsonNames(string folder, string runId)
    {
        string path = Path.Combine(folder, "run.json");
        if (!File.Exists(path) || new FileInfo(path).Length > BundleLimit)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("run_id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && string.Equals(id.GetString(), runId, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private const long BundleLimit = 1024 * 1024;
}

/// <summary>
/// <c>events_read</c> (Open): one run's ordered observations after a sequence, filtered by actor and kind. <c>max</c> defaults to
/// 500 and is capped at 5000.
/// </summary>
public sealed class EventsReadTool : LabTool<EventsReadArgs>
{
    /// <summary>
    /// Lines returned when <c>max</c> is absent.
    /// </summary>
    public const int DefaultMax = 500;

    /// <summary>
    /// The largest <c>max</c> a caller may ask for.
    /// </summary>
    public const int MaxCap = 5000;

    private readonly ILabObservationSink _sink;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the tool over the shared observation sink and the run index roots.
    /// </summary>
    public EventsReadTool(ILabObservationSink sink, LabPaths paths, IPathValidator validator)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    public override string Name => "events_read";

    /// <inheritdoc />
    public override string Description => "Ordered observations of one run (invoke, complete, fault and note lines). Filters: after_seq, actor, kind; max up to 5000. next_seq pages the stream.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(EventsReadArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        long after = args.AfterSeq ?? 0;
        int max = args.Max ?? DefaultMax;
        if (after < 0 || max < 1 || max > MaxCap)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] after_seq must be 0 or more and max between 1 and 5000"));
        }

        ObservationKind? kind = null;
        if (args.Kind is not null)
        {
            if (!TryParseKind(args.Kind, out ObservationKind parsed))
            {
                return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] kind is not an observation kind"));
            }

            kind = parsed;
        }

        Result<string> folder = RunIndex.Find([_paths.UatRuns, _paths.Jobs], args.RunId, _validator);
        if (folder.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(folder.Error));
        }

        IReadOnlyList<LabObservation> lines = _sink.Read(args.RunId, after, max);
        long next = lines.Count > 0 ? lines[^1].Seq : after;
        IReadOnlyList<LabObservation> events = lines
            .Where(line => args.Actor is null || string.Equals(line.Actor, args.Actor, StringComparison.Ordinal))
            .Where(line => kind is null || line.Kind == kind)
            .ToArray();

        return Task.FromResult(Result<object>.Success(new EventsReadResult(events, next)));
    }

    private static bool TryParseKind(string text, out ObservationKind kind)
    {
        // Snake-case names (fault_prepare) match the enum names (FaultPrepare) once underscores are dropped.
        string compact = text.Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(compact, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }
}
