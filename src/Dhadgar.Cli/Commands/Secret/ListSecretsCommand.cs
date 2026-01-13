using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class ListSecretsCommand
{
    public static async Task<int> ExecuteAsync(string category, bool reveal, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        var secretsUrl = config.EffectiveSecretsUrl;
        var endpoint = category.ToLowerInvariant() switch
        {
            "oauth" => "/api/v1/secrets/oauth",
            "betterauth" => "/api/v1/secrets/betterauth",
            "infrastructure" => "/api/v1/secrets/infrastructure",
            _ => null
        };

        if (endpoint is null)
        {
            AnsiConsole.MarkupLine($"[red]Invalid category:[/] {category}");
            AnsiConsole.MarkupLine("[dim]Valid categories:[/] oauth, betterauth, infrastructure");
            return 1;
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Loading {category} secrets...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);
                var response = await client.GetAsync<SecretsResponse>(
                    new Uri($"{secretsUrl.TrimEnd('/')}{endpoint}"),
                    ct);

                if (response?.Secrets is null || response.Secrets.Count == 0)
                {
                    AnsiConsole.MarkupLine($"\n[yellow]No {category} secrets found.[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Magenta1)
                    .AddColumn("[bold]Secret Name[/]")
                    .AddColumn("[bold]Value[/]");

                foreach (var (name, value) in response.Secrets)
                {
                    var displayValue = reveal
                        ? $"[dim]{value}[/]"
                        : new string('•', Math.Min(value.Length, 32));

                    table.AddRow(
                        $"[cyan]{name}[/]",
                        displayValue);
                }

                var header = reveal
                    ? $" {category.ToUpperInvariant()} Secrets [red](REVEALED)[/] "
                    : $" {category.ToUpperInvariant()} Secrets (Masked) ";

                var panel = new Panel(table)
                {
                    Border = BoxBorder.Double,
                    BorderStyle = new Style(reveal ? Color.Red : Color.Magenta1),
                    Header = new PanelHeader(header, Justify.Left)
                };

                AnsiConsole.Write(panel);

                if (!reveal)
                {
                    AnsiConsole.MarkupLine($"\n[dim]Use [cyan]--reveal[/] to show actual values[/]");
                }

                AnsiConsole.MarkupLine($"[dim]Total: {response.Secrets.Count} secret(s)[/]");
            });

        return 0;
    }

    public sealed record SecretsResponse(
        [property: JsonPropertyName("secrets")] Dictionary<string, string>? Secrets);
}
