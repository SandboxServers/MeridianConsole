using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Dhadgar.Shared;

/// <summary>
/// Provides argument validation helper methods.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Ensures that a value is not null.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The non-null value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static T NotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
        return value;
    }

    /// <summary>
    /// Ensures that a string is not null or empty.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The non-null, non-empty string.</returns>
    /// <exception cref="ArgumentException">Thrown when the string is null or empty.</exception>
    public static string NotNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Value cannot be null or empty.", paramName);
        }
        return value;
    }

    /// <summary>
    /// Ensures that a string is not null, empty, or whitespace.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The non-null, non-empty, non-whitespace string.</returns>
    /// <exception cref="ArgumentException">Thrown when the string is null, empty, or whitespace.</exception>
    public static string NotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", paramName);
        }
        return value;
    }

    /// <summary>
    /// Ensures that a value is greater than a specified minimum.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The minimum value (exclusive).</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The value if it is greater than the minimum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not greater than the minimum.</exception>
    public static int GreaterThan(
        int value,
        int min,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= min)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be greater than {min}.");
        }
        return value;
    }

    /// <summary>
    /// Ensures that a value is greater than or equal to a specified minimum.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The minimum value (inclusive).</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The value if it is greater than or equal to the minimum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than the minimum.</exception>
    public static int GreaterThanOrEqualTo(
        int value,
        int min,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be greater than or equal to {min}.");
        }
        return value;
    }

    /// <summary>
    /// Ensures that a value is less than a specified maximum.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="max">The maximum value (exclusive).</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The value if it is less than the maximum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not less than the maximum.</exception>
    public static int LessThan(
        int value,
        int max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value >= max)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be less than {max}.");
        }
        return value;
    }

    /// <summary>
    /// Ensures that a value is less than or equal to a specified maximum.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="max">The maximum value (inclusive).</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The value if it is less than or equal to the maximum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is greater than the maximum.</exception>
    public static int LessThanOrEqualTo(
        int value,
        int max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value > max)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be less than or equal to {max}.");
        }
        return value;
    }

    /// <summary>
    /// Ensures that a value is within a specified range.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The minimum value (inclusive).</param>
    /// <param name="max">The maximum value (inclusive).</param>
    /// <param name="paramName">The name of the parameter.</param>
    /// <returns>The value if it is within the range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside the range.</exception>
    public static int InRange(
        int value,
        int min,
        int max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be between {min} and {max} (inclusive).");
        }
        return value;
    }
}
