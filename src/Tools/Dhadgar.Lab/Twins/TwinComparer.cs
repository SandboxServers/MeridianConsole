using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Twins;

/// <summary>
/// One pointer that did not hold: the value in each world (null when the pointer does not resolve there).
/// </summary>
public sealed record TwinDiff(string Locator, JsonElement? Base, JsonElement? Variant);

/// <summary>
/// The outcome of comparing two projections. <see cref="Held"/> is true when every comparison held.
/// </summary>
public sealed record TwinComparison(bool Held, IReadOnlyList<TwinDiff> Diffs);

/// <summary>
/// Compares the base and variant projections of one experiment (C19). Values are normalised first: GUIDs, ISO timestamps,
/// <c>duration_ms</c> and 64-hex thumbprints are masked, so two worlds that differ only in those compare equal.
/// </summary>
public static class TwinComparer
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex TimestampPattern = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?",
        RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex DurationPattern = new(
        "\"duration_ms\"\\s*:\\s*-?\\d+(\\.\\d+)?",
        RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex ThumbprintPattern = new(
        @"\b[0-9a-fA-F]{64}\b",
        RegexOptions.CultureInvariant, MatchTimeout);

    private const string Missing = "<missing>";

    /// <summary>
    /// Compares the projections under the experiment's <c>[[compare]]</c> rules. A regex timeout is a failure, never a held relation.
    /// </summary>
    /// <param name="spec">The parsed experiment (its compares and relation).</param>
    /// <param name="baseProjection">The base world's projection.</param>
    /// <param name="variantProjection">The variant world's projection.</param>
    public static Result<TwinComparison> Compare(TwinSpec spec, JsonElement baseProjection, JsonElement variantProjection)
    {
        ArgumentNullException.ThrowIfNull(spec);

        try
        {
            var diffs = new List<TwinDiff>();
            foreach (TwinCompare compare in spec.Compares)
            {
                if (compare.Op == "disjoint" && compare.VariantLocators is { Count: 2 } pair)
                {
                    CompareDisjoint(pair[0], pair[1], variantProjection, diffs);
                    continue;
                }

                if (compare.Locator is null)
                {
                    return Result<TwinComparison>.Failure("[Lab.InvalidArgument] a compare has no pointer");
                }

                JsonElement? baseValue = Resolve(baseProjection, compare.Locator);
                JsonElement? variantValue = Resolve(variantProjection, compare.Locator);
                if (Normalise(baseValue) != Normalise(variantValue))
                {
                    diffs.Add(new TwinDiff(compare.Locator, baseValue, variantValue));
                }
            }

            return Result<TwinComparison>.Success(new TwinComparison(diffs.Count == 0, diffs));
        }
        catch (RegexMatchTimeoutException)
        {
            return Result<TwinComparison>.Failure("[Lab.Unavailable] the comparison timed out");
        }
    }

    /// <summary>
    /// Two lists in the variant world share no element. A list that does not resolve to an array does not hold.
    /// </summary>
    private static void CompareDisjoint(string first, string second, JsonElement variant, List<TwinDiff> diffs)
    {
        JsonElement? a = Resolve(variant, first);
        JsonElement? b = Resolve(variant, second);
        if (a is not { ValueKind: JsonValueKind.Array } listA || b is not { ValueKind: JsonValueKind.Array } listB)
        {
            diffs.Add(new TwinDiff(first, null, a));
            return;
        }

        var inB = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement element in listB.EnumerateArray())
        {
            inB.Add(Normalise(element));
        }

        var overlap = new List<JsonElement>();
        foreach (JsonElement element in listA.EnumerateArray())
        {
            if (inB.Contains(Normalise(element)))
            {
                overlap.Add(element);
            }
        }

        if (overlap.Count > 0)
        {
            diffs.Add(new TwinDiff(first, null, JsonSerializer.SerializeToElement(overlap)));
        }
    }

    /// <summary>
    /// Resolves an RFC 6901 pointer. Returns null when a segment does not exist.
    /// </summary>
    internal static JsonElement? Resolve(JsonElement root, string pointer)
    {
        JsonElement current = root;
        if (pointer.Length == 0)
        {
            return current;
        }

        foreach (string raw in pointer.Split('/')[1..])
        {
            string segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    if (!current.TryGetProperty(segment, out JsonElement child))
                    {
                        return null;
                    }

                    current = child;
                    break;
                case JsonValueKind.Array:
                    if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                        || index < 0 || index >= current.GetArrayLength())
                    {
                        return null;
                    }

                    current = current[index];
                    break;
                default:
                    return null;
            }
        }

        return current;
    }

    /// <summary>
    /// The comparable text of a value: its raw JSON with the masked forms replaced. A missing value is a fixed marker.
    /// </summary>
    internal static string Normalise(JsonElement? value) => value is { } element ? Normalise(element) : Missing;

    private static string Normalise(JsonElement element)
    {
        string text = element.GetRawText();
        text = GuidPattern.Replace(text, "<guid>");
        text = TimestampPattern.Replace(text, "<timestamp>");
        text = DurationPattern.Replace(text, "\"duration_ms\":\"<duration>\"");
        text = ThumbprintPattern.Replace(text, "<thumbprint>");
        return text;
    }
}
