using System.Text.Json;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// Heal data of a proxy disable: the proxy to re-enable.
/// </summary>
public sealed record ProxyHealData(string Proxy);

/// <summary>
/// Heal data of a toxic: the proxy and the toxic name (<c>&lt;fault_id&gt;-&lt;stream&gt;</c>).
/// </summary>
public sealed record ToxicHealData(string Proxy, string Toxic);

/// <summary>
/// Heal data of a partition: every proxy on the edge.
/// </summary>
public sealed record PartitionHealData(IReadOnlyList<string> Proxies);

/// <summary>
/// Shared helpers for the Toxiproxy fault kinds: heal data round trips and the "already gone" rule.
/// </summary>
internal static class KindSupport
{
    /// <summary>
    /// Serializes heal data into the journal element.
    /// </summary>
    public static Result<JsonElement> ToHealData<T>(T value)
    {
        try
        {
            return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(value, LabJson.Options));
        }
        catch (JsonException)
        {
            return Result<JsonElement>.Failure("[Lab.FaultKind] heal data could not be written");
        }
    }

    /// <summary>
    /// Reads heal data back from the journal. A journal entry that does not parse is a failure, never a guess.
    /// </summary>
    public static Result<T> FromHealData<T>(JsonElement healData) where T : class
    {
        try
        {
            T? value = JsonSerializer.Deserialize<T>(healData.GetRawText(), LabJson.Options);
            return value is null
                ? Result<T>.Failure("[Lab.FaultKind] heal data is empty")
                : Result<T>.Success(value);
        }
        catch (JsonException)
        {
            return Result<T>.Failure("[Lab.FaultKind] heal data is unreadable");
        }
    }

    /// <summary>
    /// True when a Toxiproxy failure is "not found": the thing a heal wants to undo is already gone, which heal treats as done.
    /// </summary>
    public static bool IsNotFound(Result result) =>
        result.IsFailure && result.Error.StartsWith("[Lab.NotFound]", StringComparison.Ordinal);
}
