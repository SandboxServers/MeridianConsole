using Dhadgar.Cli.Configuration;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Auth;

public sealed class LogoutCommand
{
    public static Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] You are not currently authenticated.");
            return Task.FromResult(0);
        }

        // Clear authentication tokens
        config.AccessToken = null;
        config.RefreshToken = null;
        config.TokenExpiresAt = null;
        config.CurrentOrgId = null;
        config.Save();

        var panel = new Panel(new Markup(
            "[green]✓[/] Successfully logged out\n" +
            "[dim]Your authentication tokens have been cleared[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(2, 1),
            Header = new PanelHeader(" Logged Out ", Justify.Left)
        };

        AnsiConsole.Write(panel);

        AnsiConsole.MarkupLine("\n[dim]Run [cyan]dhadgar auth login[/] to authenticate again[/]");

        return Task.FromResult(0);
    }
}
