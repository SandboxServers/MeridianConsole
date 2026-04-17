using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Shared.Data;

/// <summary>
/// Shared database utility methods for EF Core operations.
/// </summary>
public static class DatabaseHelpers
{
    /// <summary>
    /// Determines whether a <see cref="DbUpdateException"/> was caused by a unique constraint violation.
    /// </summary>
    public static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Escapes special characters in a LIKE pattern to prevent SQL injection via ILike/Like.
    /// </summary>
    public static string EscapeLikePattern(string input) =>
        input.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}
