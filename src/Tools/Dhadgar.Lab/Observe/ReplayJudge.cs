using System.Security.Cryptography;
using System.Text;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// Compares a stored fingerprint with the current one and decides whether a replay reproduced its original (C18).
/// Grades only; L25 adds the primary signatures to the comparison.
/// </summary>
public static class ReplayJudge
{
    /// <summary>
    /// The number of attempts a <c>schedule_approximate</c> replay may use.
    /// </summary>
    public const int MaxApproximateAttempts = 5;

    private const string HashedMarker = "sha256:";

    /// <summary>
    /// One text per differing field. Values that could hold a path (spec files, build id, baseline, mutant) are hashed.
    /// </summary>
    public static IReadOnlyList<string> Diff(LabFingerprint stored, LabFingerprint current)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(current);

        var diffs = new List<string>();
        Compare(diffs, "schema", stored.Schema.ToString(), current.Schema.ToString(), hash: false);
        Compare(diffs, "commit", stored.Commit, current.Commit, hash: false);
        Compare(diffs, "dirty", stored.Dirty.ToString(), current.Dirty.ToString(), hash: false);
        Compare(diffs, "build_id", stored.BuildId, current.BuildId, hash: true);
        Compare(diffs, "baseline", stored.Baseline, current.Baseline, hash: true);
        Compare(diffs, "mutant", stored.Mutant, current.Mutant, hash: true);
        CompareMaps(diffs, "image_digests", stored.ImageDigests, current.ImageDigests, hash: false);
        Compare(diffs, "lock_files_sha256", stored.LockFilesSha256, current.LockFilesSha256, hash: false);
        CompareMaps(diffs, "effective_config_sha256", stored.EffectiveConfigSha256, current.EffectiveConfigSha256, hash: false);
        CompareMaps(diffs, "specs", ToMap(stored.Specs), ToMap(current.Specs), hash: true);
        Compare(diffs, "daemon_version", stored.DaemonVersion, current.DaemonVersion, hash: false);
        Compare(diffs, "toxiproxy_version", stored.ToxiproxyVersion, current.ToxiproxyVersion, hash: false);
        Compare(diffs, "seed", stored.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), current.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), hash: false);
        Compare(diffs, "os", stored.Os, current.Os, hash: true);
        CompareMaps(diffs, "engine_versions", stored.EngineVersions, current.EngineVersions, hash: false);
        return diffs;
    }

    /// <summary>
    /// Whether a replay reproduced its original, by fidelity: <c>exact</c> needs the same grades on the first attempt;
    /// <c>schedule_approximate</c> needs them on one of up to five attempts; <c>evidence_only</c> gives <c>null</c>.
    /// </summary>
    /// <param name="fidelity">The original run's fidelity.</param>
    /// <param name="originalGrades">The original run's grade list, in row order.</param>
    /// <param name="attemptGrades">The replay's grade lists, one per attempt, in order.</param>
    public static bool? Reproduced(ReplayFidelity fidelity, IReadOnlyList<string> originalGrades, IReadOnlyList<IReadOnlyList<string>> attemptGrades)
    {
        ArgumentNullException.ThrowIfNull(originalGrades);
        ArgumentNullException.ThrowIfNull(attemptGrades);

        switch (fidelity)
        {
            case ReplayFidelity.Exact:
                return attemptGrades.Count > 0 && SameGrades(originalGrades, attemptGrades[0]);

            case ReplayFidelity.ScheduleApproximate:
                return attemptGrades.Take(MaxApproximateAttempts).Any(attempt => SameGrades(originalGrades, attempt));

            default:
                return null;
        }
    }

    private static bool SameGrades(IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
        expected.SequenceEqual(actual, StringComparer.Ordinal);

    private static void Compare(List<string> diffs, string field, string? stored, string? current, bool hash)
    {
        if (string.Equals(stored, current, StringComparison.Ordinal))
        {
            return;
        }

        diffs.Add($"{field}: {Render(stored, hash)} -> {Render(current, hash)}");
    }

    private static void CompareMaps(List<string> diffs, string field, IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, string> current, bool hash)
    {
        foreach (string key in stored.Keys.Union(current.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            stored.TryGetValue(key, out string? storedValue);
            current.TryGetValue(key, out string? currentValue);
            Compare(diffs, $"{field}.{key}", storedValue, currentValue, hash);
        }
    }

    private static Dictionary<string, string> ToMap(IEnumerable<SpecHash> specs) =>
        specs.ToDictionary(spec => Render(spec.File, hash: true), spec => spec.Sha256, StringComparer.Ordinal);

    private static string Render(string? value, bool hash)
    {
        if (value is null)
        {
            return "absent";
        }

        if (!hash)
        {
            return value;
        }

        return HashedMarker + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
    }
}
