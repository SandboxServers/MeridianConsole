using System.Text;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Random;

namespace Dhadgar.Lab.Fuzz;

/// <summary>
/// One fuzz case: the mutation's name, the request body bytes and the <c>Content-Type</c> to send. Case <c>n</c> of a target
/// is fully determined by the run seed and <c>n</c> (R-SEED), so any single case replays alone.
/// </summary>
/// <param name="Index">The case index <c>n</c>.</param>
/// <param name="Mutation">The mutation name, with its sub-kind after a colon (<c>boundary:64KiB</c>).</param>
/// <param name="Body">The request body bytes, which may be invalid JSON on purpose.</param>
/// <param name="ContentType">The <c>Content-Type</c> header value to send.</param>
public sealed record FuzzCase(int Index, string Mutation, ReadOnlyMemory<byte> Body, string ContentType);

/// <summary>
/// The seeded mutators (L28 file 2). They start from <see cref="FuzzTargets.ValidBody"/> and apply one mutation chosen by
/// <c>LabRandom(seed, "fuzz:&lt;target&gt;:&lt;n&gt;")</c>. No <c>System.Random</c> or <c>Guid.NewGuid()</c> feeds a case.
/// </summary>
public static class Mutators
{
    /// <summary>The <c>Content-Type</c> of a valid request.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>The mutation kinds, in the order the chooser indexes them. Changing the order changes replay.</summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "drop-property",
        "null-property",
        "swap-type",
        "boundary",
        "numeric-extreme",
        "extra-property",
        "duplicate-key",
        "truncated-json",
        "wrong-content-type",
        "empty-body",
    ];

    /// <summary>
    /// The boundary strings (label, value). The 64 KiB and 1 MiB cases are required on <c>enroll</c> (L28 amendment, D-ML76).
    /// The 1 MiB value is sized so the whole JSON body stays under <c>LabHttp.MaxRequestBytes</c> (1 MiB).
    /// </summary>
    public static readonly IReadOnlyList<(string Label, string Value)> BoundaryStrings =
    [
        ("empty", string.Empty),
        ("64KiB", new string('a', 64 * 1024)),
        ("1MiB-minus-envelope", new string('b', 1024 * 1024 - 256)),
        ("NUL", "\u0000"),
        ("control", "\u0001\u0002\u001F\u007F"),
        ("RTL", "‮evil‬"),
        ("combining", "é́́́́́́́"),
        ("traversal", "../../etc/passwd"),
        ("percent-NUL", "%00"),
    ];

    private static readonly IReadOnlyList<string> WrongContentTypes =
        ["text/plain", "application/xml", "application/x-www-form-urlencoded"];

    /// <summary>
    /// Builds case <paramref name="index"/> of <paramref name="target"/> for <paramref name="seed"/>. The same seed and index
    /// always give the same bytes.
    /// </summary>
    public static FuzzCase Build(FuzzTarget target, ulong seed, int index)
    {
        ArgumentNullException.ThrowIfNull(target);
        var rng = new LabRandom(seed, "fuzz:" + target.Name + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        byte[] valid = FuzzTargets.ValidBody(target);
        string kind = Kinds[rng.Next(0, Kinds.Count)];
        return Apply(kind, index, valid, rng);
    }

    private static FuzzCase Apply(string kind, int index, byte[] valid, LabRandom rng)
    {
        switch (kind)
        {
            case "truncated-json":
                return new FuzzCase(index, kind, valid.AsMemory(0, valid.Length / 2), JsonContentType);
            case "wrong-content-type":
                return new FuzzCase(index, kind, valid, WrongContentTypes[rng.Next(0, WrongContentTypes.Count)]);
            case "empty-body":
                return new FuzzCase(index, kind, ReadOnlyMemory<byte>.Empty, JsonContentType);
            case "duplicate-key":
                return new FuzzCase(index, kind, DuplicateFirstKey(valid), JsonContentType);
        }

        var root = (JsonObject)JsonNode.Parse(valid)!;
        List<string> names = root.Select(pair => pair.Key).ToList();
        string name = names[rng.Next(0, names.Count)];
        string mutation = kind;

        switch (kind)
        {
            case "drop-property":
                _ = root.Remove(name);
                break;
            case "null-property":
                root[name] = null;
                break;
            case "swap-type":
                root[name] = SwapType(rng.Next(0, 4));
                break;
            case "boundary":
                (string label, string value) = BoundaryStrings[rng.Next(0, BoundaryStrings.Count)];
                root[name] = value;
                mutation = "boundary:" + label;
                break;
            case "numeric-extreme":
                root[name] = NumericExtreme(rng.Next(0, 4));
                break;
            case "extra-property":
                root["unknown_" + rng.Next(0, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)] = "extra";
                break;
        }

        return new FuzzCase(index, mutation, Encoding.UTF8.GetBytes(root.ToJsonString()), JsonContentType);
    }

    private static JsonNode? SwapType(int which) => which switch
    {
        0 => JsonValue.Create(12345L),
        1 => JsonValue.Create("text-where-a-number-belongs"),
        2 => new JsonArray(JsonValue.Create(1L), JsonValue.Create("two")),
        _ => new JsonObject { ["nested"] = "object" },
    };

    private static JsonValue NumericExtreme(int which) => which switch
    {
        0 => JsonValue.Create(long.MaxValue)!,
        1 => JsonValue.Create(long.MinValue)!,
        2 => JsonValue.Create(double.MaxValue)!,
        _ => JsonValue.Create(-0.0d)!,
    };

    /// <summary>Repeats the first property name with a different value, as raw text (a duplicate key the parser must tolerate or refuse).</summary>
    private static byte[] DuplicateFirstKey(byte[] valid)
    {
        var root = (JsonObject)JsonNode.Parse(valid)!;
        string first = root.First().Key;
        string text = Encoding.UTF8.GetString(valid);
        string duplicated = "{\"" + first + "\":\"duplicate\"," + text[1..];
        return Encoding.UTF8.GetBytes(duplicated);
    }
}
