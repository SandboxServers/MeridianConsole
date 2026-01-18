using System.Text.RegularExpressions;
using Spectre.Console;

namespace Dhadgar.Cli.Commands;

internal static partial class CommandValidation
{
    [GeneratedRegex("^[a-zA-Z0-9-]{3,24}$")]
    private static partial Regex VaultNamePattern();

    /// <summary>
    /// Validates and outputs error message to console if invalid.
    /// </summary>
    public static bool TryValidateVaultName(string vaultName)
    {
        var (isValid, errorMessage) = ValidateVaultName(vaultName);
        if (!isValid && errorMessage is not null)
        {
            AnsiConsole.MarkupLine(errorMessage);
        }
        return isValid;
    }

    /// <summary>
    /// Validates vault name without console output.
    /// Returns (isValid, errorMessage) tuple.
    /// </summary>
    internal static (bool IsValid, string? ErrorMessage) ValidateVaultName(string? vaultName)
    {
        if (string.IsNullOrWhiteSpace(vaultName) || !VaultNamePattern().IsMatch(vaultName))
        {
            return (false, "[red]Invalid vault name.[/]\n[dim]Use 3-24 characters with letters, numbers, and hyphens.[/]");
        }
        return (true, null);
    }
}
