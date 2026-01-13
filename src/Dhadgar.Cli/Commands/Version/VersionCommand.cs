using System.Reflection;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Version;

public sealed class VersionCommand
{
    public static Task<int> ExecuteAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var buildDate = GetMetadataValue(assembly, "BuildDateUtc");
        var breakingChange = GetMetadataValue(assembly, "BreakingChangeUtc");
        var version = assembly.GetName().Version?.ToString() ?? "unknown";

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn("[bold]Field[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Assembly Version", version);
        table.AddRow("Build Date (UTC)", string.IsNullOrWhiteSpace(buildDate) ? "unknown" : buildDate);
        table.AddRow("Last Breaking Change (UTC)", string.IsNullOrWhiteSpace(breakingChange) ? "unknown" : breakingChange);

        AnsiConsole.Write(table);

        return Task.FromResult(0);
    }

    private static string? GetMetadataValue(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => string.Equals(attr.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }
}
