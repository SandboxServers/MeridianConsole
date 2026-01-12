using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Org;

public sealed class ListOrgsCommand
{
    public static async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        var identityUrl = config.EffectiveIdentityUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading organizations...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);
                var orgs = await client.GetAsync<List<OrgListItem>>(
                    $"{identityUrl.TrimEnd('/')}/organizations",
                    ct);

                if (orgs is null || orgs.Count == 0)
                {
                    AnsiConsole.MarkupLine("\n[yellow]No organizations found.[/]");
                    AnsiConsole.MarkupLine("[dim]Create one with:[/] [cyan]dhadgar org create <name>[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Blue)
                    .AddColumn("[bold]ID[/]")
                    .AddColumn("[bold]Name[/]")
                    .AddColumn("[bold]Role[/]")
                    .AddColumn("[bold]Status[/]");

                foreach (var org in orgs)
                {
                    var statusMarker = org.IsActive ? "[green]●[/]" : "[dim]○[/]";
                    var roleColor = org.Role?.ToLowerInvariant() switch
                    {
                        "owner" => "yellow",
                        "admin" => "blue",
                        "member" => "green",
                        _ => "dim"
                    };

                    var currentMarker = config.CurrentOrgId == org.Id?.ToString()
                        ? " [cyan]← current[/]"
                        : "";

                    table.AddRow(
                        $"[dim]{org.Id?.ToString()[..8]}...[/]",
                        $"[bold]{org.Name}[/]{currentMarker}",
                        $"[{roleColor}]{org.Role}[/]",
                        statusMarker);
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total: {orgs.Count} organization(s)[/]");
            });

        return 0;
    }

    private sealed record OrgListItem(
        [property: JsonPropertyName("id")] Guid? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("isActive")] bool IsActive);
}
