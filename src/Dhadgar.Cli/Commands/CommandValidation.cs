using System.Text.RegularExpressions;
using Spectre.Console;

namespace Dhadgar.Cli.Commands;

internal static class CommandValidation
{
    private static readonly Regex VaultNameRegex = new("^[a-zA-Z0-9-]{3,24}$", RegexOptions.Compiled);

    public static bool TryValidateVaultName(string vaultName)
    {
        if (string.IsNullOrWhiteSpace(vaultName) || !VaultNameRegex.IsMatch(vaultName))
        {
            AnsiConsole.MarkupLine("[red]Invalid vault name.[/]");
            AnsiConsole.MarkupLine("[dim]Use 3-24 characters with letters, numbers, and hyphens.[/]");
            return false;
        }

        return true;
    }
}
