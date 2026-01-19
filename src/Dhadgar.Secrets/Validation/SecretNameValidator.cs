using System.Text.RegularExpressions;

namespace Dhadgar.Secrets.Validation;

/// <summary>
/// Validates secret names for Azure Key Vault compatibility and security.
/// Key Vault naming rules: alphanumeric and dashes, 1-127 characters.
/// </summary>
public static partial class SecretNameValidator
{
    /// <summary>
    /// Azure Key Vault compatible pattern: alphanumeric and dashes,
    /// must start and end with alphanumeric character.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?$", RegexOptions.Compiled)]
    private static partial Regex ValidNamePattern();

    /// <summary>
    /// Single character names are valid (just alphanumeric, no dash).
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9]$", RegexOptions.Compiled)]
    private static partial Regex SingleCharPattern();

    /// <summary>
    /// Validates a secret name for Key Vault compatibility and security.
    /// </summary>
    public static ValidationResult Validate(string? secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return ValidationResult.Failure("Secret name is required");
        }

        if (secretName.Length > 127)
        {
            return ValidationResult.Failure("Secret name must be 127 characters or fewer");
        }

        // Check valid pattern
        if (secretName.Length == 1)
        {
            if (!SingleCharPattern().IsMatch(secretName))
            {
                return ValidationResult.Failure("Single character secret name must be alphanumeric");
            }
        }
        else if (!ValidNamePattern().IsMatch(secretName))
        {
            return ValidationResult.Failure(
                "Secret name must contain only alphanumeric characters and dashes, " +
                "and must start and end with an alphanumeric character");
        }

        // Security checks - block injection patterns
        if (ContainsInjectionPattern(secretName))
        {
            return ValidationResult.Failure("Secret name contains invalid characters or patterns");
        }

        return ValidationResult.Success();
    }

    private static bool ContainsInjectionPattern(string name)
    {
        // Note: The regex already restricts to alphanumeric + dash,
        // but these checks provide defense in depth.

        // Path traversal (shouldn't pass regex, but belt-and-suspenders)
        if (name.Contains("..", StringComparison.Ordinal) ||
            name.Contains('/', StringComparison.Ordinal) ||
            name.Contains('\\', StringComparison.Ordinal))
            return true;

        // Null bytes
        if (name.Contains('\0', StringComparison.Ordinal))
            return true;

        // While regex blocks these, explicitly check for common injection chars
        if (name.Contains('\'', StringComparison.Ordinal) ||
            name.Contains(';', StringComparison.Ordinal) ||
            name.Contains('<', StringComparison.Ordinal) ||
            name.Contains('>', StringComparison.Ordinal))
            return true;

        return false;
    }
}

/// <summary>
/// Result of secret name validation.
/// </summary>
public readonly struct ValidationResult : IEquatable<ValidationResult>
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }

    private ValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Failure(string message) => new(false, message);

    public bool Equals(ValidationResult other) =>
        IsValid == other.IsValid && ErrorMessage == other.ErrorMessage;

    public override bool Equals(object? obj) =>
        obj is ValidationResult other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(IsValid, ErrorMessage);

    public static bool operator ==(ValidationResult left, ValidationResult right) =>
        left.Equals(right);

    public static bool operator !=(ValidationResult left, ValidationResult right) =>
        !left.Equals(right);
}
