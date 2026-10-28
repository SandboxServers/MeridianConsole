using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// The SHA-256 of one spec file (C18 fingerprint part).
/// </summary>
public sealed record SpecHash(string File, string Sha256);

/// <summary>
/// The reproducibility fingerprint of one run (C18). Holds hashes and identifiers only, never a secret value.
/// <c>Baseline</c> and <c>Mutant</c> stay null until L60 and L54.
/// </summary>
public sealed record LabFingerprint(
    int Schema,
    string Commit,
    bool Dirty,
    string? BuildId,
    string? Baseline,
    string? Mutant,
    IReadOnlyDictionary<string, string> ImageDigests,
    string LockFilesSha256,
    IReadOnlyDictionary<string, string> EffectiveConfigSha256,
    IReadOnlyList<SpecHash> Specs,
    string DaemonVersion,
    string ToxiproxyVersion,
    ulong Seed,
    string Os,
    IReadOnlyDictionary<string, string> EngineVersions);

/// <summary>
/// The parts a fingerprint is built from. Every part is already collected; <see cref="FingerprintBuilder.Build"/> only hashes.
/// </summary>
/// <param name="ServiceEnvironments">Service name to its effective environment, values as they will run.</param>
/// <param name="SecretKeys">Environment keys whose values are secrets; they are hashed as <c>vault:&lt;key&gt;</c>.</param>
public sealed record FingerprintInput(
    string Commit,
    bool Dirty,
    string? BuildId,
    IReadOnlyDictionary<string, string> ImageDigests,
    string? LockFilesSha256,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ServiceEnvironments,
    IReadOnlySet<string> SecretKeys,
    IReadOnlyList<SpecHash> Specs,
    string? DaemonVersion,
    string? ToxiproxyVersion,
    ulong Seed,
    string? Os,
    IReadOnlyDictionary<string, string> EngineVersions);

/// <summary>
/// Builds a <see cref="LabFingerprint"/> from collected parts. A missing part becomes <c>unknown</c>; it never fails the run.
/// </summary>
public static class FingerprintBuilder
{
    /// <summary>
    /// The fingerprint schema version.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// The text used for a part that could not be collected.
    /// </summary>
    public const string UnknownPart = "unknown";

    private const string LockFileName = "packages.lock.json";

    /// <summary>
    /// Builds the fingerprint. Missing text parts are <c>unknown</c>; secret environment values are replaced by their keys before hashing.
    /// </summary>
    public static LabFingerprint Build(FingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var configs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>> service in input.ServiceEnvironments)
        {
            configs[service.Key] = EffectiveConfigSha256(service.Value, input.SecretKeys);
        }

        return new LabFingerprint(
            SchemaVersion,
            input.Commit,
            input.Dirty,
            input.BuildId,
            Baseline: null,
            Mutant: null,
            new Dictionary<string, string>(input.ImageDigests, StringComparer.Ordinal),
            input.LockFilesSha256 ?? UnknownPart,
            configs,
            input.Specs.OrderBy(spec => spec.File, StringComparer.Ordinal).ToArray(),
            input.DaemonVersion ?? UnknownPart,
            input.ToxiproxyVersion ?? UnknownPart,
            input.Seed,
            input.Os ?? UnknownPart,
            new Dictionary<string, string>(input.EngineVersions, StringComparer.Ordinal));
    }

    /// <summary>
    /// SHA-256 (lowercase hex) over the sorted <c>key=value</c> lines of an environment. Keys in <paramref name="secretKeys"/> hash as <c>vault:&lt;key&gt;</c>.
    /// </summary>
    public static string EffectiveConfigSha256(IReadOnlyDictionary<string, string> environment, IReadOnlySet<string> secretKeys)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(secretKeys);

        var builder = new StringBuilder();
        foreach (string key in environment.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            string value = secretKeys.Contains(key) ? "vault:" + key : environment[key];
            builder.Append(key).Append('=').Append(value).Append('\n');
        }

        return Hex(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    /// <summary>
    /// SHA-256 (lowercase hex) over the sorted <c>&lt;relative path&gt;\n&lt;sha256&gt;\n</c> lines of every
    /// <c>packages.lock.json</c> under <paramref name="sourceTree"/>, skipping build output folders.
    /// </summary>
    /// <param name="sourceTree">The build's source tree.</param>
    /// <param name="validator">Validates the tree against the allowed roots.</param>
    /// <param name="allowedRoots">The roots the tree may sit under.</param>
    public static Result<string> LockFilesSha256(string sourceTree, IPathValidator validator, IEnumerable<string> allowedRoots)
    {
        ArgumentNullException.ThrowIfNull(validator);
        Result<string> root = validator.ValidatePath(sourceTree ?? string.Empty, allowedRoots);
        if (root.IsFailure)
        {
            return Result<string>.Failure(root.Error);
        }

        if (!Directory.Exists(root.Value))
        {
            return Result<string>.Failure("[Lab.NotFound] the source tree does not exist");
        }

        try
        {
            var lines = new List<(string Relative, string Hash)>();
            foreach (string file in Directory.EnumerateFiles(root.Value, LockFileName, SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root.Value, file).Replace('\\', '/');
                if (relative.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules" or ".git"))
                {
                    continue;
                }

                lines.Add((relative, Hex(SHA256.HashData(File.ReadAllBytes(file)))));
            }

            var builder = new StringBuilder();
            foreach ((string relative, string hash) in lines.OrderBy(line => line.Relative, StringComparer.Ordinal))
            {
                builder.Append(relative).Append('\n').Append(hash).Append('\n');
            }

            return Result<string>.Success(Hex(Encoding.UTF8.GetBytes(builder.ToString())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure("[Lab.Unavailable] a lock file could not be read");
        }
    }

    /// <summary>
    /// The daemon's informational version, or <c>unknown</c> when the assembly carries none.
    /// </summary>
    public static string DaemonVersionText()
    {
        string? version = typeof(FingerprintBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? UnknownPart : version;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}

/// <summary>
/// Hashes a <see cref="LabFingerprint"/> over its canonical JSON (sorted keys, no whitespace).
/// </summary>
public static class FingerprintHasher
{
    /// <summary>
    /// The canonical hash of <paramref name="fingerprint"/>, lowercase hex.
    /// </summary>
    public static string Sha256(LabFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        JsonNode? node = JsonSerializer.SerializeToNode(fingerprint, ObserveJson.Options);
        string canonical = Canonicalise(node)?.ToJsonString() ?? "null";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static JsonNode? Canonicalise(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (string key in obj.Select(pair => pair.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray())
                {
                    sorted[key] = Canonicalise(obj[key]);
                }

                return sorted;

            case JsonArray array:
                var items = new JsonArray();
                foreach (JsonNode? item in array)
                {
                    items.Add(Canonicalise(item));
                }

                return items;

            default:
                return node?.DeepClone();
        }
    }
}
