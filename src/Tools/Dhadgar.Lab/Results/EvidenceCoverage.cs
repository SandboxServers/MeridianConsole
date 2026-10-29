using System.Text.Json.Nodes;

namespace Dhadgar.Lab.Results;

/// <summary>
/// The evidence the results index holds, in one place (D-ML93). Built kinds are the signature kinds the ingest stores;
/// not-built kinds and not-ingested tables are named so that a reader never takes "known" for "everything checked".
/// </summary>
public static class EvidenceCoverage
{
    /// <summary>Signature kinds the ingest stores: clause and inv from L25, exc and http from L25e, hist from L25f.</summary>
    public static readonly IReadOnlyList<string> BuiltKinds = ["clause", "inv", "exc", "http", "hist"];

    /// <summary>C12 signature kinds the ingest does not store yet.</summary>
    public static readonly IReadOnlyList<string> NotBuiltKinds = ["twin", "peer", "env", "ver"];

    /// <summary>Index tables or evidence the ingest does not fill yet. L25f stored faults, pairs and history runs, so it is empty.</summary>
    public static readonly IReadOnlyList<string> NotIngested = [];

    /// <summary>The note every payload carries about partial coverage.</summary>
    public const string Note =
        "partial: known means every built signature matched one open issue; not_built_kinds are not in the index yet";

    /// <summary>The coverage object: <c>{"built_kinds","not_built_kinds","not_ingested","note"}</c>.</summary>
    public static JsonObject ToJson() =>
        new()
        {
            ["built_kinds"] = new JsonArray(BuiltKinds.Select(k => (JsonNode)k).ToArray()),
            ["not_built_kinds"] = new JsonArray(NotBuiltKinds.Select(k => (JsonNode)k).ToArray()),
            ["not_ingested"] = new JsonArray(NotIngested.Select(k => (JsonNode)k).ToArray()),
            ["note"] = Note,
        };
}
