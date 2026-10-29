using System.Globalization;
using Dhadgar.Lab.Soak;
using Dhadgar.Shared.Results;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The limits of I7, read from <c>docs/guides/uat-specs/invariants/soak.toml</c> (<c>[limits]</c> table).
/// </summary>
/// <param name="MemoryMibPerHour">The largest allowed least-squares memory growth of a container, in MiB per hour.</param>
/// <param name="PidsPerHour">The largest allowed least-squares process-count growth of a container, per hour.</param>
public sealed record SoakLimits(double MemoryMibPerHour, double PidsPerHour)
{
    /// <summary>
    /// Reads the limits. A missing file, a parse error or a missing or non-numeric key is a failure.
    /// </summary>
    /// <param name="path">The path of <c>soak.toml</c>.</param>
    public static Result<SoakLimits> Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<SoakLimits>.Failure("[Lab.NotFound] the soak limits file could not be read");
        }

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (Exception exception) when (exception is TomlException or ArgumentException or InvalidOperationException)
        {
            return Result<SoakLimits>.Failure("[Lab.InvalidArgument] the soak limits file is not valid TOML");
        }

        if (!root.TryGetValue("limits", out object? raw) || raw is not TomlTable limits)
        {
            return Result<SoakLimits>.Failure("[Lab.InvalidArgument] the soak limits file has no [limits] table");
        }

        if (!TryNumber(limits, "memory_mib_per_hour", out double memory) || !TryNumber(limits, "pids_per_hour", out double pids))
        {
            return Result<SoakLimits>.Failure("[Lab.InvalidArgument] the soak limits are missing or not numbers");
        }

        return Result<SoakLimits>.Success(new SoakLimits(memory, pids));
    }

    private static bool TryNumber(TomlTable table, string key, out double value)
    {
        value = 0;
        if (!table.TryGetValue(key, out object? raw))
        {
            return false;
        }

        switch (raw)
        {
            case long integer:
                value = integer;
                return true;
            case double real:
                value = real;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// I7 (C11, L29): no resource trend over a soak. Per container, the least-squares slope of memory (MiB) and of the process
/// count, taken over the soak's samples, must stay within <see cref="SoakLimits"/>. Outside a soak, or with fewer than three
/// samples, the check is a skip (A3), never a pass. A sampler failure is a check error.
/// </summary>
public sealed class I7ResourceTrendInvariant : ISkippableInvariant
{
    /// <summary>The fewest samples a series needs before its slope is judged.</summary>
    public const int MinSamples = 3;

    /// <summary>The source named by a skip.</summary>
    public const string SkipSource = "soak";

    private readonly ResourceSampleStore _store;
    private readonly Func<Result<SoakLimits>> _limits;

    /// <summary>
    /// Creates the check over the soak's sample store and the limits. The limits are read at each check.
    /// </summary>
    public I7ResourceTrendInvariant(ResourceSampleStore store, Func<Result<SoakLimits>> limits)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <inheritdoc />
    public string Id => "I7";

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        Result<InvariantOutcome> outcome = await CheckWithSkipsAsync(window, ct).ConfigureAwait(false);
        return outcome.IsFailure
            ? Result<IReadOnlyList<InvariantViolation>>.Failure(outcome.Error)
            : Result<IReadOnlyList<InvariantViolation>>.Success(outcome.Value!.Violations);
    }

    /// <inheritdoc />
    public Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);
        ct.ThrowIfCancellationRequested();

        if (!_store.IsSoaking)
        {
            return Task.FromResult(Skip("no soak is running"));
        }

        string? error = _store.LastError;
        if (error is not null)
        {
            return Task.FromResult(Result<InvariantOutcome>.Failure("[Lab.Docker] resource sampler failed: " + error));
        }

        IReadOnlyList<ResourceSampleSet> samples = _store.Snapshot();
        if (samples.Count < MinSamples)
        {
            return Task.FromResult(Skip("fewer than 3 samples"));
        }

        Result<SoakLimits> limits = _limits();
        if (limits.IsFailure)
        {
            return Task.FromResult(Result<InvariantOutcome>.Failure(limits.Error));
        }

        var violations = new List<InvariantViolation>();
        bool judged = false;
        foreach (string series in samples.SelectMany(s => s.Values.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            (string kind, string name) = Split(series);
            double? limit = kind switch
            {
                ResourceSampler.MemorySeries => limits.Value!.MemoryMibPerHour,
                ResourceSampler.PidsSeries => limits.Value!.PidsPerHour,
                _ => null,
            };
            if (limit is null)
            {
                continue;
            }

            var points = samples
                .Where(s => s.Values.ContainsKey(series))
                .Select(s => ((s.At - samples[0].At).TotalHours, s.Values[series]))
                .ToArray();
            if (points.Length < MinSamples)
            {
                continue;
            }

            judged = true;
            double slope = Slope(points);
            if (slope > limit.Value)
            {
                violations.Add(new InvariantViolation(
                    Id,
                    name,
                    "trend",
                    $"{kind} of {name} rises {slope.ToString("0.0", CultureInfo.InvariantCulture)} per hour over the soak, above the limit {limit.Value.ToString("0.#", CultureInfo.InvariantCulture)} per hour",
                    window.To,
                    null));
            }
        }

        if (!judged)
        {
            return Task.FromResult(Skip("fewer than 3 samples"));
        }

        return Task.FromResult(Result<InvariantOutcome>.Success(new InvariantOutcome(violations, [])));
    }

    private Result<InvariantOutcome> Skip(string reason) =>
        Result<InvariantOutcome>.Success(new InvariantOutcome([], [new InvariantSkip(Id, SkipSource, reason)]));

    private static (string Kind, string Name) Split(string series)
    {
        int colon = series.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? (series, string.Empty) : (series[..colon], series[(colon + 1)..]);
    }

    /// <summary>The least-squares slope of value against hours; zero when every sample has the same time.</summary>
    internal static double Slope((double X, double Y)[] points)
    {
        double meanX = points.Average(p => p.X);
        double meanY = points.Average(p => p.Y);
        double numerator = 0;
        double denominator = 0;
        foreach ((double x, double y) in points)
        {
            numerator += (x - meanX) * (y - meanY);
            denominator += (x - meanX) * (x - meanX);
        }

        return denominator == 0 ? 0 : numerator / denominator;
    }
}
