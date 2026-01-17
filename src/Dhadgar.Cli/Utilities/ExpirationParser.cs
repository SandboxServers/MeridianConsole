namespace Dhadgar.Cli.Utilities;

/// <summary>
/// Parses expiration time strings into DateTime values.
/// Supports relative formats (1h, 7d) and ISO 8601 dates.
/// </summary>
internal static class ExpirationParser
{
    /// <summary>
    /// Parses an expiration string into a DateTime.
    /// </summary>
    /// <param name="input">Expiration string (e.g., "1h", "7d", "2024-12-31")</param>
    /// <param name="utcNow">Optional UTC now for testing; defaults to DateTime.UtcNow</param>
    /// <returns>Parsed DateTime in UTC, or null if parsing fails</returns>
    public static DateTime? Parse(string? input, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var now = utcNow ?? DateTime.UtcNow;
        var trimmed = input.Trim().ToLowerInvariant();

        // Try relative formats: 1h, 12h, 1d, 7d, 30d, etc.
        if (trimmed.EndsWith('h') && int.TryParse(trimmed[..^1], out var hours) && hours > 0)
        {
            return now.AddHours(hours);
        }

        if (trimmed.EndsWith('d') && int.TryParse(trimmed[..^1], out var days) && days > 0)
        {
            return now.AddDays(days);
        }

        if (trimmed.EndsWith('w') && int.TryParse(trimmed[..^1], out var weeks) && weeks > 0)
        {
            return now.AddDays(weeks * 7);
        }

        if (trimmed.EndsWith('m') && int.TryParse(trimmed[..^1], out var months) && months > 0)
        {
            return now.AddMonths(months);
        }

        // Try ISO 8601 date parsing
        if (DateTime.TryParse(input.Trim(), out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    /// <summary>
    /// Validates that an expiration string is in a valid format.
    /// </summary>
    public static bool IsValid(string? input)
    {
        return Parse(input) is not null;
    }
}
