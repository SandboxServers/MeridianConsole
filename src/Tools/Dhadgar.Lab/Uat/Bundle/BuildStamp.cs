using System.Text.Json.Nodes;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// The build a bundle was produced against: the id, the commit and the dirty flag, and the checkout relative to the repo root.
/// </summary>
/// <param name="Id">The build id, or <c>nobuild</c>.</param>
/// <param name="Commit">The full commit hash, or empty when not collected.</param>
/// <param name="Dirty">True when the checkout had uncommitted changes.</param>
/// <param name="Checkout">The checkout relative to the repo root with <c>/</c> separators (<c>.</c> for the root), or empty.</param>
public sealed record BuildStamp(string Id, string Commit, bool Dirty, string Checkout)
{
    /// <summary>The stamp of a run with no build.</summary>
    public static readonly BuildStamp None = new("nobuild", string.Empty, false, string.Empty);

    /// <summary>
    /// The first eight characters of a build id (the whole id when it is shorter).
    /// </summary>
    /// <param name="id">The build id.</param>
    public static string Build8Of(string id) => id.Length <= 8 ? id : id[..8];

    /// <summary>
    /// The build line for <c>ledger.md</c>: <c>nobuild</c>, the eight-character id when the commit is unknown, or the id with the commit and the dirty flag.
    /// </summary>
    public string LedgerLine
    {
        get
        {
            if (Id == "nobuild")
            {
                return "nobuild";
            }

            if (Commit.Length == 0)
            {
                return Build8Of(Id);
            }

            string commit = Commit.Length <= 12 ? Commit : Commit[..12];
            return $"{Build8Of(Id)} (commit {commit}, {(Dirty ? "dirty" : "clean")})";
        }
    }

    /// <summary>
    /// Reads <c>run["build"]</c> with fields <c>id</c>, <c>commit</c>, <c>dirty</c> and <c>checkout</c>. A missing or wrong-typed field
    /// falls back to that field of <see cref="None"/>. Never throws.
    /// </summary>
    /// <param name="run">The parsed <c>run.json</c>.</param>
    public static BuildStamp FromRunJson(JsonObject run)
    {
        ArgumentNullException.ThrowIfNull(run);
        JsonObject? build = run["build"] as JsonObject;
        return new BuildStamp(
            Text(build, "id") ?? None.Id,
            Text(build, "commit") ?? None.Commit,
            Flag(build, "dirty") ?? None.Dirty,
            Text(build, "checkout") ?? None.Checkout);
    }

    private static string? Text(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static bool? Flag(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
}
