using System.Text.RegularExpressions;

namespace Dhadgar.Mods.Services;

/// <summary>
/// Represents a semantic version (semver) with comparison support.
/// </summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }
    public string? BuildMetadata { get; }

    private static readonly Regex SemverPattern = SemverRegex();

    public SemanticVersion(int major, int minor, int patch, string? prerelease = null, string? buildMetadata = null)
    {
        if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
        if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
    }

    public static bool TryParse(string version, out SemanticVersion? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        // Remove leading 'v' if present
        if (version.StartsWith('v') || version.StartsWith('V'))
            version = version[1..];

        var match = SemverPattern.Match(version);
        if (!match.Success)
            return false;

        var major = int.Parse(match.Groups["major"].Value);
        var minor = int.Parse(match.Groups["minor"].Value);
        var patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0;
        var prerelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null;
        var buildMetadata = match.Groups["buildmetadata"].Success ? match.Groups["buildmetadata"].Value : null;

        result = new SemanticVersion(major, minor, patch, prerelease, buildMetadata);
        return true;
    }

    public static SemanticVersion Parse(string version)
    {
        if (!TryParse(version, out var result))
            throw new FormatException($"Invalid semantic version: {version}");
        return result!;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0) return majorCompare;

        var minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0) return minorCompare;

        var patchCompare = Patch.CompareTo(other.Patch);
        if (patchCompare != 0) return patchCompare;

        // Prerelease versions have lower precedence than normal versions
        if (Prerelease is null && other.Prerelease is not null) return 1;
        if (Prerelease is not null && other.Prerelease is null) return -1;
        if (Prerelease is null && other.Prerelease is null) return 0;

        return string.Compare(Prerelease, other.Prerelease, StringComparison.Ordinal);
    }

    public bool Satisfies(string constraint)
    {
        return VersionRangeParser.Satisfies(this, constraint);
    }

    public override string ToString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Prerelease))
            version += $"-{Prerelease}";
        if (!string.IsNullOrEmpty(BuildMetadata))
            version += $"+{BuildMetadata}";
        return version;
    }

    public override bool Equals(object? obj) =>
        obj is SemanticVersion other && CompareTo(other) == 0;

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, Prerelease);

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) =>
        left?.CompareTo(right) == 0 || (left is null && right is null);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) =>
        !(left == right);

    public static bool operator <(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) >= 0;

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)(\.(?<patch>\d+))?(-(?<prerelease>[0-9A-Za-z\-\.]+))?(\+(?<buildmetadata>[0-9A-Za-z\-\.]+))?$")]
    private static partial Regex SemverRegex();
}
