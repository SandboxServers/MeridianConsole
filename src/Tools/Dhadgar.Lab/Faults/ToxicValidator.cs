using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults;

/// <summary>
/// Checks a toxic before Toxiproxy sees it: the type, the stream, the toxicity, and for each type the only attributes it
/// accepts with their ranges. Anything else is <c>[Lab.InvalidArgument]</c>.
/// </summary>
public static class ToxicValidator
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, AttributeRange>> Allowed =
        new Dictionary<string, IReadOnlyDictionary<string, AttributeRange>>(StringComparer.Ordinal)
        {
            ["latency"] = Ranges(("latency", 0, 60000), ("jitter", 0, 60000)),
            ["bandwidth"] = Ranges(("rate", 0, 1000000)),
            ["timeout"] = Ranges(("timeout", 0, 900000)),
            ["reset_peer"] = Ranges(("timeout", 0, 900000)),
            ["slow_close"] = Ranges(("delay", 0, 60000)),
            ["slicer"] = Ranges(("average_size", 1, 65536), ("size_variation", 0, 65536), ("delay", 0, 1000000)),
            ["limit_data"] = Ranges(("bytes", 0, 1073741824)),
        };

    /// <summary>
    /// Validates one toxic. Attributes not listed for the type are refused, even when their names are otherwise known.
    /// </summary>
    /// <param name="type">The toxic type.</param>
    /// <param name="stream"><c>upstream</c> or <c>downstream</c>.</param>
    /// <param name="toxicity">0.0 to 1.0 (NaN is refused).</param>
    /// <param name="attributes">The type-specific attributes.</param>
    public static Result Validate(string type, string stream, double toxicity, IReadOnlyDictionary<string, long> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!Allowed.TryGetValue(type, out var ranges))
        {
            return Result.Failure("[Lab.InvalidArgument] unknown toxic type");
        }

        if (stream is not ("upstream" or "downstream"))
        {
            return Result.Failure("[Lab.InvalidArgument] stream must be upstream or downstream");
        }

        if (!(toxicity >= 0.0 && toxicity <= 1.0))
        {
            return Result.Failure("[Lab.InvalidArgument] toxicity must be between 0.0 and 1.0");
        }

        foreach (var (name, value) in attributes)
        {
            if (!ranges.TryGetValue(name, out var range))
            {
                return Result.Failure("[Lab.InvalidArgument] attribute is not allowed for this toxic type");
            }

            if (value < range.Min || value > range.Max)
            {
                return Result.Failure($"[Lab.InvalidArgument] attribute is outside its range ({range.Min} to {range.Max})");
            }
        }

        return Result.Success();
    }

    private static Dictionary<string, AttributeRange> Ranges(params (string Name, long Min, long Max)[] entries) =>
        entries.ToDictionary(entry => entry.Name, entry => new AttributeRange(entry.Min, entry.Max), StringComparer.Ordinal);

    private sealed record AttributeRange(long Min, long Max);
}
