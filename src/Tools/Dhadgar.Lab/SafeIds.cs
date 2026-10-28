using System.Linq;

namespace Dhadgar.Lab;

/// <summary>
/// Checks for ids that become part of a path (build ids, folder names).
/// </summary>
internal static class SafeIds
{
    /// <summary>
    /// True for a 1-64 character id whose first character is an ASCII letter or digit and whose other characters are ASCII letters, digits, <c>-</c>, <c>.</c> or <c>_</c>.
    /// </summary>
    /// <param name="id">The candidate id.</param>
    internal static bool IsSafe(string? id)
    {
        if (id is null || id.Length is 0 or > 64 || !char.IsAsciiLetterOrDigit(id[0]))
        {
            return false;
        }

        return id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_');
    }
}
