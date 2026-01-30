using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Dhadgar.Shared.Extensions;

/// <summary>
/// Extension methods for string manipulation.
/// </summary>
public static partial class StringExtensions
{
    /// <summary>
    /// Converts a string to a URL-safe slug.
    /// </summary>
    /// <param name="value">The string to convert.</param>
    /// <returns>A URL-safe slug.</returns>
    /// <remarks>
    /// This method:
    /// - Converts to lowercase
    /// - Removes diacritics (accents)
    /// - Replaces spaces and underscores with hyphens
    /// - Removes all non-alphanumeric characters except hyphens
    /// - Collapses multiple hyphens into one
    /// - Trims leading and trailing hyphens
    /// </remarks>
    public static string ToSlug(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Convert to lowercase
        value = value.ToLowerInvariant();

        // Remove diacritics (accents)
        value = RemoveDiacritics(value);

        // Replace spaces and underscores with hyphens
        value = value.Replace(' ', '-').Replace('_', '-');

        // Remove all non-alphanumeric characters except hyphens
        value = InvalidCharsRegex().Replace(value, string.Empty);

        // Collapse multiple hyphens into one
        value = MultipleHyphensRegex().Replace(value, "-");

        // Trim leading and trailing hyphens
        value = value.Trim('-');

        return value;
    }

    /// <summary>
    /// Truncates a string to the specified maximum length.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <param name="maxLength">The maximum length.</param>
    /// <param name="suffix">The suffix to append if truncated (default: "...").</param>
    /// <returns>The truncated string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxLength is less than the suffix length.</exception>
    public static string Truncate(this string value, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (maxLength < suffix.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength),
                $"maxLength must be at least {suffix.Length} to accommodate the suffix.");
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - suffix.Length)] + suffix;
    }

    /// <summary>
    /// Safely extracts a substring without throwing exceptions.
    /// </summary>
    /// <param name="value">The string to extract from.</param>
    /// <param name="start">The starting index.</param>
    /// <param name="length">The length of the substring.</param>
    /// <returns>
    /// The substring if valid indices are provided; otherwise, an empty string
    /// or a substring adjusted to valid bounds.
    /// </returns>
    public static string SafeSubstring(this string value, int start, int length)
    {
        if (string.IsNullOrEmpty(value) || start >= value.Length || start < 0 || length <= 0)
        {
            return string.Empty;
        }

        // Adjust length if it exceeds the string boundary
        if (start + length > value.Length)
        {
            length = value.Length - start;
        }

        return value.Substring(start, length);
    }

    /// <summary>
    /// Removes diacritics (accents) from a string.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <returns>The string with diacritics removed.</returns>
    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[^a-z0-9\-]", RegexOptions.Compiled)]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex(@"-{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleHyphensRegex();
}
