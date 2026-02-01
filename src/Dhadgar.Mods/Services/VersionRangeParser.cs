namespace Dhadgar.Mods.Services;

/// <summary>
/// Parses and evaluates version range expressions.
/// Supports: ^1.2.3, ~1.2.3, >=1.0.0, <=2.0.0, >1.0.0, <2.0.0, 1.2.3, >=1.0.0 <2.0.0
/// </summary>
public static class VersionRangeParser
{

    public static bool Satisfies(SemanticVersion version, string constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint))
            return true;

        constraint = constraint.Trim();

        // Handle compound ranges (space-separated)
        var parts = constraint.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (!SatisfiesSingle(version, part.Trim()))
                return false;
        }

        return true;
    }

    private static bool SatisfiesSingle(SemanticVersion version, string constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint))
            return true;

        // Handle caret range (^1.2.3 means >=1.2.3 and <2.0.0)
        if (constraint.StartsWith('^'))
        {
            var rangeVersion = constraint[1..];
            if (!SemanticVersion.TryParse(rangeVersion, out var minVersion))
                return false;

            // Version must be >= minVersion
            if (version < minVersion)
                return false;

            // Version must be < next major
            if (version.Major > minVersion!.Major)
                return false;

            // For 0.x.y, caret means >=0.x.y <0.(x+1).0
            // For 0.0.y, caret means >=0.0.y <0.0.(y+1) (patch-level only)
            if (minVersion.Major == 0)
            {
                if (minVersion.Minor == 0)
                {
                    // ^0.0.x allows only patch-level changes
                    if (version.Minor != 0 || version.Patch > minVersion.Patch)
                        return false;
                }
                else if (version.Minor > minVersion.Minor)
                {
                    return false;
                }
            }

            return true;
        }

        // Handle tilde range (~1.2.3 means >=1.2.3 and <1.3.0)
        if (constraint.StartsWith('~'))
        {
            var rangeVersion = constraint[1..];
            if (!SemanticVersion.TryParse(rangeVersion, out var minVersion))
                return false;

            // Version must be >= minVersion
            if (version < minVersion)
                return false;

            // Version must be < next minor
            if (version.Major > minVersion!.Major)
                return false;
            if (version.Major == minVersion.Major && version.Minor > minVersion.Minor)
                return false;

            return true;
        }

        // Handle comparison operators
        if (constraint.StartsWith(">="))
        {
            var rangeVersion = constraint[2..].Trim();
            if (!SemanticVersion.TryParse(rangeVersion, out var minVersion))
                return false;
            return version >= minVersion;
        }

        if (constraint.StartsWith("<="))
        {
            var rangeVersion = constraint[2..].Trim();
            if (!SemanticVersion.TryParse(rangeVersion, out var maxVersion))
                return false;
            return version <= maxVersion;
        }

        if (constraint.StartsWith('>'))
        {
            var rangeVersion = constraint[1..].Trim();
            if (!SemanticVersion.TryParse(rangeVersion, out var minVersion))
                return false;
            return version > minVersion;
        }

        if (constraint.StartsWith('<'))
        {
            var rangeVersion = constraint[1..].Trim();
            if (!SemanticVersion.TryParse(rangeVersion, out var maxVersion))
                return false;
            return version < maxVersion;
        }

        // Handle exact version
        if (SemanticVersion.TryParse(constraint, out var exactVersion))
        {
            return version == exactVersion;
        }

        // Handle wildcard (1.2.* or 1.*)
        if (constraint.Contains('*'))
        {
            var wildcardParts = constraint.Split('.');
            if (wildcardParts.Length >= 1 && int.TryParse(wildcardParts[0], out var major))
            {
                if (version.Major != major) return false;
            }
            if (wildcardParts.Length >= 2 && wildcardParts[1] != "*" && int.TryParse(wildcardParts[1], out var minor))
            {
                if (version.Minor != minor) return false;
            }
            return true;
        }

        return false;
    }
}
