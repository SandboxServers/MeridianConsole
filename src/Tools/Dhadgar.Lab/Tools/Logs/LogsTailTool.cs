using System.Globalization;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Logs;

/// <summary>
/// <c>logs_tail</c> arguments (C4). Snake-case on the wire through <see cref="LabJson"/>.
/// </summary>
public sealed record LogsTailArgs(string Process, string? Since = null, string? Contains = null, string? Level = null, string? Category = null, int? MaxLines = null);

/// <summary>
/// One line of a <c>logs_tail</c> result.
/// </summary>
public sealed record LogsTailLine(DateTimeOffset Ts, string Level, string Category, int? EventId, string Message);

/// <summary>
/// The <c>logs_tail</c> result: <c>{lines:[...]}</c>.
/// </summary>
public sealed record LogsTailResult(IReadOnlyList<LogsTailLine> Lines);

/// <summary>
/// <c>logs_tail</c> (Open): the most recent captured lines of one process, filtered. Lines are already redacted when stored.
/// </summary>
public sealed class LogsTailTool : LabTool<LogsTailArgs>
{
    /// <summary>
    /// Lines returned when <c>max_lines</c> is absent.
    /// </summary>
    public const int DefaultMaxLines = 200;

    /// <summary>
    /// The largest <c>max_lines</c> a caller may ask for.
    /// </summary>
    public const int MaxLinesCap = 2000;

    /// <summary>
    /// The process name that has no captured log; it yields an empty result.
    /// </summary>
    public const string DaemonProcess = "daemon";

    private const int MaxRelativeAmount = 1_000_000;

    private readonly LabProcessSupervisor _supervisor;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the tool over the supervisor's captured logs.
    /// </summary>
    public LogsTailTool(LabProcessSupervisor supervisor, TimeProvider time)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public override string Name => "logs_tail";

    /// <inheritdoc />
    public override string Description => "Recent captured log lines of a lab process (service, infrastructure, daemon or member:<id>). Filters: since (ISO or -<n>m / -<n>s), contains, level, category; max_lines up to 2000.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(LogsTailArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.IsNullOrWhiteSpace(args.Process))
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] process is required"));
        }

        int max = args.MaxLines ?? DefaultMaxLines;
        if (max < 1 || max > MaxLinesCap)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] max_lines must be between 1 and 2000"));
        }

        DateTimeOffset? since = null;
        if (args.Since is not null)
        {
            Result<DateTimeOffset> parsed = ParseSince(args.Since, _time.GetUtcNow());
            if (parsed.IsFailure)
            {
                return Task.FromResult(Result<object>.Failure(parsed.Error));
            }

            since = parsed.Value;
        }

        IReadOnlyList<LogLine> lines = [];
        if (!string.Equals(args.Process, DaemonProcess, StringComparison.Ordinal))
        {
            Result<IReadOnlyList<LogLine>> captured = _supervisor.ReadLines(args.Process);
            if (captured.IsFailure)
            {
                return Task.FromResult(Result<object>.Failure(captured.Error));
            }

            lines = captured.Value!;
        }

        var matching = lines.Where(line =>
                (since is null || line.Timestamp >= since)
                && (args.Level is null || string.Equals(line.Level, args.Level, StringComparison.OrdinalIgnoreCase))
                && (args.Category is null || string.Equals(line.Category, args.Category, StringComparison.OrdinalIgnoreCase))
                && (args.Contains is null || line.Message.Contains(args.Contains, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        IEnumerable<LogsTailLine> tail = matching
            .Skip(Math.Max(0, matching.Count - max))
            .Select(line => new LogsTailLine(line.Timestamp, line.Level, line.Category, line.EventId, line.Message));

        return Task.FromResult(Result<object>.Success(new LogsTailResult(tail.ToList())));
    }

    /// <summary>
    /// Parses <c>since</c>: an ISO 8601 timestamp, or <c>-&lt;n&gt;m</c> / <c>-&lt;n&gt;s</c> relative to <paramref name="now"/>.
    /// </summary>
    internal static Result<DateTimeOffset> ParseSince(string text, DateTimeOffset now)
    {
        string trimmed = text.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '-' && (trimmed[^1] == 'm' || trimmed[^1] == 's')
            && int.TryParse(trimmed.AsSpan(1, trimmed.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int amount))
        {
            if (amount < 0 || amount > MaxRelativeAmount)
            {
                return Result<DateTimeOffset>.Failure("[Lab.InvalidArgument] since is out of range");
            }

            return Result<DateTimeOffset>.Success(trimmed[^1] == 'm' ? now.AddMinutes(-amount) : now.AddSeconds(-amount));
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset iso))
        {
            return Result<DateTimeOffset>.Success(iso);
        }

        return Result<DateTimeOffset>.Failure("[Lab.InvalidArgument] since must be ISO 8601 or -<n>m / -<n>s");
    }
}
