using System.CommandLine;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Help;

public sealed class CommandsCommand
{
    public static Task<int> ExecuteAsync(Command root)
    {
        AnsiConsole.Write(new Rule("[bold blue]COMMANDS[/]").RuleStyle("blue").Centered());

        var entries = EnumerateCommands(root).ToList();
        var usageWrapWidth = Math.Max(50, AnsiConsole.Profile.Width - 40);
        var descriptionWrapWidth = Math.Max(30, AnsiConsole.Profile.Width - 70);

        foreach (var topLevel in root.Subcommands)
        {
            var group = entries
                .Where(entry => entry.Path.StartsWith($"{topLevel.Name}", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (group.Count == 0)
            {
                continue;
            }

            var groupTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Command[/]"))
                .AddColumn(new TableColumn("[bold]Usage[/]"))
                .AddColumn(new TableColumn("[bold]Description[/]"));

            foreach (var entry in group)
            {
                var usage = BuildUsage(entry.Path, entry.Command);
                var usageWrapped = WrapText(usage, usageWrapWidth);
                var description = string.IsNullOrWhiteSpace(entry.Command.Description)
                    ? "-"
                    : entry.Command.Description;
                var descriptionWrapped = WrapText(description, descriptionWrapWidth);

                groupTable.AddRow(
                    $"[cyan]{Markup.Escape(entry.Path)}[/]",
                    $"[green]{Markup.Escape(usageWrapped)}[/]",
                    $"[dim]{Markup.Escape(descriptionWrapped)}[/]");
            }

            var panel = new Panel(groupTable)
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Blue),
                Header = new PanelHeader($" {topLevel.Name.ToUpperInvariant()} ", Justify.Left)
            };

            AnsiConsole.Write(panel);
        }

        AnsiConsole.MarkupLine("\n[dim]Use [cyan]dhadgar <command> --help[/] for details[/]");

        return Task.FromResult(0);
    }

    private static IEnumerable<(string Path, Command Command)> EnumerateCommands(Command root)
    {
        foreach (var child in root.Subcommands)
        {
            var path = child.Name;
            yield return (path, child);

            foreach (var nested in EnumerateCommands(child, path))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<(string Path, Command Command)> EnumerateCommands(Command command, string parentPath)
    {
        foreach (var child in command.Subcommands)
        {
            var path = $"{parentPath} {child.Name}";
            yield return (path, child);

            foreach (var nested in EnumerateCommands(child, path))
            {
                yield return nested;
            }
        }
    }

    private static string BuildUsage(string path, Command command)
    {
        var parts = new List<string> { "dhadgar", path };

        foreach (var argument in command.Arguments)
        {
            parts.Add(FormatArgument(argument));
        }

        foreach (var option in command.Options)
        {
            parts.Add(FormatOption(option));
        }

        return string.Join(' ', parts);
    }

    private static string FormatArgument(Argument argument)
    {
        var name = string.IsNullOrWhiteSpace(argument.Name) ? "value" : argument.Name;
        return argument.Arity.MinimumNumberOfValues == 0
            ? $"[{name}]"
            : $"<{name}>";
    }

    private static string FormatOption(Option option)
    {
        var alias = option.Aliases.FirstOrDefault(a => a.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                    ?? option.Aliases.FirstOrDefault()
                    ?? $"--{option.Name}";

        if (option.Arity.MaximumNumberOfValues == 0)
        {
            return $"[{alias}]";
        }

        var valueName = string.IsNullOrWhiteSpace(option.ArgumentHelpName)
            ? option.Name
            : option.ArgumentHelpName;

        var valueToken = option.Arity.MinimumNumberOfValues == 0
            ? $"[{valueName}]"
            : $"<{valueName}>";

        return $"[{alias} {valueToken}]";
    }

    private static string WrapText(string text, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0 || text.Length <= maxWidth)
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new List<string>();
        var currentLength = 0;

        foreach (var word in words)
        {
            var extra = current.Count == 0 ? word.Length : word.Length + 1;
            if (currentLength + extra > maxWidth)
            {
                lines.Add(string.Join(' ', current));
                current.Clear();
                currentLength = 0;
            }

            current.Add(word);
            currentLength += extra;
        }

        if (current.Count > 0)
        {
            lines.Add(string.Join(' ', current));
        }

        return string.Join('\n', lines);
    }
}
