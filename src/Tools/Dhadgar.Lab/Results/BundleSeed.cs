using System.Text.Json.Nodes;

namespace Dhadgar.Lab.Results;

/// <summary>
/// The seed of a bundle (C17: seeds are <c>ulong</c>). SQLite has no unsigned 64-bit type, so the results DB stores the seed's
/// bit pattern in its <c>long</c> columns (D-ML135). Readers that show a seed convert back with <see cref="FromStored"/>.
/// </summary>
internal static class BundleSeed
{
    /// <summary>Reads run.json's seed (C17: ulong). Null when absent or not a non-negative integer.</summary>
    internal static ulong? ReadSeed(JsonObject run) =>
        run["seed"] is JsonValue value && value.TryGetValue(out ulong seed) ? seed : null;

    /// <summary>The results DB column form: the seed's bit pattern (SQLite has no unsigned 64-bit type).</summary>
    internal static long ToStored(ulong seed) => unchecked((long)seed);

    /// <summary>The seed a stored column holds.</summary>
    internal static ulong FromStored(long stored) => unchecked((ulong)stored);
}
