using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>log</c> source (C7): lines of one process from its <see cref="LogRing"/>, filtered by <c>contains</c> or <c>matches</c>,
/// <c>category</c>, <c>level</c>, <c>event_id</c> and <c>since</c>. Result shape: <c>{matches, count}</c>.
/// </summary>
public sealed class LogSource : IClauseSource
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<string, LogRing?> _ringFor;

    /// <summary>
    /// Creates the source over a lookup from process name to its log ring.
    /// </summary>
    public LogSource(Func<string, LogRing?> ringFor)
    {
        _ringFor = ringFor ?? throw new ArgumentNullException(nameof(ringFor));
    }

    /// <inheritdoc />
    public string Name => "log";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Result<string> process = ClauseFields.Required(clause.SourceFields, "process");
        if (process.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(process.Error));
        }

        Result<DateTimeOffset> since = window.SinceFor(clause.Since);
        if (since.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(since.Error));
        }

        LogRing? ring = _ringFor(process.Value);
        if (ring is null)
        {
            return Task.FromResult(Result<JsonElement>.Failure($"[Lab.Unavailable] no log for process {process.Value}"));
        }

        Result<JsonElement> filtered = Filter(clause, ring, since.Value);
        return Task.FromResult(filtered);
    }

    private static Result<JsonElement> Filter(UatClause clause, LogRing ring, DateTimeOffset since)
    {
        Result<string> contains = ClauseFields.Optional(clause.SourceFields, "contains");
        Result<string> matches = ClauseFields.Optional(clause.SourceFields, "matches");
        Result<string> category = ClauseFields.Optional(clause.SourceFields, "category");
        Result<string> level = ClauseFields.Optional(clause.SourceFields, "level");
        if (contains.IsFailure || matches.IsFailure || category.IsFailure || level.IsFailure)
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] log filter fields must be strings");
        }

        if (contains.Value.Length == 0 && matches.Value.Length == 0)
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] log needs contains or matches");
        }

        Regex? regex = null;
        if (matches.Value.Length > 0)
        {
            try
            {
                regex = new Regex(matches.Value, RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                return Result<JsonElement>.Failure("[Lab.InvalidArgument] invalid regex: " + ex.Message);
            }
        }

        int? eventId = null;
        if (clause.SourceFields.TryGetValue("event_id", out JsonElement rawEventId))
        {
            if (!rawEventId.TryGetInt32(out int parsed))
            {
                return Result<JsonElement>.Failure("[Lab.InvalidArgument] event_id must be an integer");
            }

            eventId = parsed;
        }

        var matched = new List<object>();
        try
        {
            foreach (LogLine line in ring.Snapshot())
            {
                if (line.Timestamp < since
                    || (category.Value.Length > 0 && !string.Equals(line.Category, category.Value, StringComparison.Ordinal))
                    || (level.Value.Length > 0 && !string.Equals(line.Level, level.Value, StringComparison.OrdinalIgnoreCase))
                    || (eventId is not null && line.EventId != eventId))
                {
                    continue;
                }

                bool textHit = contains.Value.Length > 0
                    ? line.Message.Contains(contains.Value, StringComparison.Ordinal)
                    : regex!.IsMatch(line.Message);
                if (!textHit)
                {
                    continue;
                }

                matched.Add(new
                {
                    timestamp = line.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    level = line.Level,
                    category = line.Category,
                    event_id = line.EventId,
                    message = line.Message,
                });
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return Result<JsonElement>.Failure("[Lab.Unavailable] log regex timed out after 1 s");
        }

        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(new { matches = matched, count = matched.Count }));
    }
}
