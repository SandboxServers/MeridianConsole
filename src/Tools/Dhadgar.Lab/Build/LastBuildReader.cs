using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Build;

/// <summary>
/// Reads the lab's latest build (<c>builds\latest.txt</c> and <c>builds\&lt;id&gt;\build.json</c>) as a <see cref="BuildStamp"/>.
/// This is the build the lab last made, not proof of what a running service was started from.
/// </summary>
public static class LastBuildReader
{
    /// <summary>
    /// The stamp for a bundle made now: the mutant build's stamp while a mutant build serves the services (D-ML155), otherwise
    /// <see cref="Read(LabPaths, IPathValidator)"/>. Never throws.
    /// </summary>
    /// <param name="paths">The lab paths.</param>
    /// <param name="validator">Validates both files against the builds root.</param>
    /// <param name="running">The running mutant build, or null when none is registered.</param>
    public static BuildStamp Read(LabPaths paths, IPathValidator validator, RunningMutantBuild? running) =>
        running?.Current is { } mark
            ? new BuildStamp(mark.BuildId, mark.Commit, Dirty: true, Checkout: string.Empty, Mutant: mark.MutantId)
            : Read(paths, validator);

    /// <summary>
    /// Returns the latest build's stamp, or <see cref="BuildStamp.None"/> when there is none or it cannot be read. Never throws.
    /// </summary>
    /// <param name="paths">The lab paths.</param>
    /// <param name="validator">Validates both files against the builds root.</param>
    public static BuildStamp Read(LabPaths paths, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(validator);
        try
        {
            Result<string> latest = validator.ValidatePath(Path.Combine(paths.Builds, "latest.txt"), [paths.Builds]);
            if (latest.IsFailure || !File.Exists(latest.Value))
            {
                return BuildStamp.None;
            }

            string id = File.ReadAllText(latest.Value!).Trim();
            if (!SafeIds.IsSafe(id))
            {
                return BuildStamp.None;
            }

            Result<string> info = validator.ValidatePath(Path.Combine(paths.Builds, id, "build.json"), [paths.Builds]);
            if (info.IsFailure || !File.Exists(info.Value))
            {
                return BuildStamp.None;
            }

            if (JsonNode.Parse(File.ReadAllText(info.Value!)) is not JsonObject json
                || json["build_id"] is not JsonValue buildId
                || !buildId.TryGetValue(out string? storedId)
                || storedId != id)
            {
                return BuildStamp.None;
            }

            string commit = Text(json, "commit") ?? string.Empty;
            bool dirty = Flag(json, "dirty") ?? false;
            string checkout = RelativeCheckout(Text(json, "checkout") ?? string.Empty, paths.RepoRoot);
            return new BuildStamp(id, commit, dirty, checkout);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return BuildStamp.None;
        }
    }

    /// <summary>
    /// The checkout relative to the repo root with <c>/</c> separators: <c>.</c> for the root itself, empty when the repo root is
    /// unset or the checkout is outside it. An absolute path is never returned.
    /// </summary>
    /// <param name="checkout">The checkout path as written in build.json.</param>
    /// <param name="repoRoot">The repo root, or empty.</param>
    internal static string RelativeCheckout(string checkout, string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(checkout) || string.IsNullOrWhiteSpace(repoRoot))
        {
            return string.Empty;
        }

        char[] separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        string root = Path.GetFullPath(repoRoot).TrimEnd(separators);
        string full = Path.GetFullPath(checkout).TrimEnd(separators);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return ".";
        }

        string prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return full[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static bool? Flag(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
}
