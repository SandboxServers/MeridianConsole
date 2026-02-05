using System.Globalization;
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
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

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

        // Use TryParse to avoid OverflowException on large version numbers
        if (!int.TryParse(match.Groups["major"].Value, out var major))
            return false;
        if (!int.TryParse(match.Groups["minor"].Value, out var minor))
            return false;

        var patch = 0;
        if (match.Groups["patch"].Success && !int.TryParse(match.Groups["patch"].Value, out patch))
            return false;

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

        return ComparePrereleaseIdentifiers(Prerelease!, other.Prerelease!);
    }

    /// <summary>
    /// Compares prerelease identifiers per semver 2.0 specification.
    /// Rules:
    /// - Identifiers are split by dots and compared left-to-right
    /// - Numeric identifiers are compared as integers (1 < 2 < 10)
    /// - Alphanumeric identifiers are compared lexically (ASCII)
    /// - Numeric identifiers always have lower precedence than alphanumeric
    /// - Fewer identifiers have lower precedence if all preceding match (alpha < alpha.1)
    /// </summary>
    private static int ComparePrereleaseIdentifiers(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');

        var minLength = Math.Min(leftParts.Length, rightParts.Length);

        for (var i = 0; i < minLength; i++)
        {
            var leftPart = leftParts[i];
            var rightPart = rightParts[i];

            var leftIsNumeric = long.TryParse(leftPart, out var leftNum);
            var rightIsNumeric = long.TryParse(rightPart, out var rightNum);

            if (leftIsNumeric && rightIsNumeric)
            {
                // Both numeric: compare as integers
                var numCompare = leftNum.CompareTo(rightNum);
                if (numCompare != 0) return numCompare;
            }
            else if (leftIsNumeric)
            {
                // Numeric < Alphanumeric
                return -1;
            }
            else if (rightIsNumeric)
            {
                // Alphanumeric > Numeric
                return 1;
            }
            else
            {
                // Both alphanumeric: compare lexically
                var strCompare = string.Compare(leftPart, rightPart, StringComparison.Ordinal);
                if (strCompare != 0) return strCompare;
            }
        }

        // All preceding identifiers are equal; more identifiers = higher precedence
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    public bool Satisfies(string constraint)
    {
        return VersionRangeParser.Satisfies(this, constraint);
    }

    public override string ToString()
    {
        var version = string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);
        if (!string.IsNullOrEmpty(Prerelease))
            version = string.Concat(version, "-", Prerelease);
        if (!string.IsNullOrEmpty(BuildMetadata))
            version = string.Concat(version, "+", BuildMetadata);
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

    public static bool operator <(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null) return right is not null;
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null) return false;
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null) return true;
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null) return right is null;
        return left.CompareTo(right) >= 0;
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)(\.(?<patch>\d+))?(-(?<prerelease>[0-9A-Za-z\-\.]+))?(\+(?<buildmetadata>[0-9A-Za-z\-\.]+))?$")]
    private static partial Regex SemverRegex();
}
