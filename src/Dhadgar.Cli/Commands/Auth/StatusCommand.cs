using System.Globalization;
using Dhadgar.Cli.Configuration;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Auth;

public sealed class StatusCommand
{
    public static Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("Gateway URL", config.EffectiveGatewayUrl);
        table.AddRow("Identity URL", config.EffectiveIdentityUrl);
        table.AddRow("Secrets URL", config.EffectiveSecretsUrl);
        table.AddRow("Current Org ID", config.CurrentOrgId ?? "[dim]none[/]");

        if (config.IsAuthenticated())
        {
            table.AddRow("Authentication", "[green]✓ Authenticated[/]");
            table.AddRow("Token Expires", config.TokenExpiresAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "[dim]unknown[/]");
        }
        else
        {
            table.AddRow("Authentication", "[yellow]⚠ Not authenticated[/]");
            table.AddRow("Token Expires", "[dim]n/a[/]");
        }

        AnsiConsole.Write(table);

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("\n[dim]Run [cyan]dhadgar auth login[/] to authenticate[/]");
        }

        return Task.FromResult(0);
    }
}
