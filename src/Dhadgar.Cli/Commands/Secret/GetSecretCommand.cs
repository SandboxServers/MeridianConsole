using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class GetSecretCommand
{
    public static async Task<int> ExecuteAsync(string secretName, bool reveal, bool copyToClipboard, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        var secretsUrl = config.EffectiveSecretsUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Retrieving secret '{secretName}'...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);
                var response = await client.GetAsync<SecretResponse>(
                    new Uri($"{secretsUrl.TrimEnd('/')}/api/v1/secrets/{secretName}"),
                    ct);

                if (response?.Value is null)
                {
                    AnsiConsole.MarkupLine($"\n[red]Secret '{secretName}' not found or access denied.[/]");
                    return;
                }

                var displayValue = reveal
                    ? response.Value
                    : new string('•', Math.Min(response.Value.Length, 32));

                var grid = new Grid()
                    .AddColumn()
                    .AddColumn();

                grid.AddRow("[bold]Name:[/]", $"[cyan]{response.Name}[/]");
                grid.AddRow("[bold]Value:[/]", reveal ? $"[dim]{displayValue}[/]" : displayValue);
                grid.AddRow("[bold]Length:[/]", $"[dim]{response.Value.Length} characters[/]");

                var panel = new Panel(grid)
                {
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(reveal ? Color.Red : Color.Magenta1),
                    Padding = new Padding(2, 1),
                    Header = new PanelHeader(
                        reveal ? " Secret (REVEALED) " : " Secret (Masked) ",
                        Justify.Left)
                };

                AnsiConsole.Write(panel);

                if (copyToClipboard)
                {
                    try
                    {
                        // Note: Clipboard access requires platform-specific implementations
                        // This is a placeholder - actual implementation would need TextCopy or similar
                        AnsiConsole.MarkupLine("\n[yellow]⚠[/] [dim]Clipboard copy not yet implemented[/]");
                    }
                    catch
                    {
                        AnsiConsole.MarkupLine("\n[red]Failed to copy to clipboard[/]");
                    }
                }

                if (!reveal)
                {
                    AnsiConsole.MarkupLine($"\n[dim]Use [cyan]--reveal[/] to show actual value[/]");
                }
            });

        return 0;
    }

    public sealed record SecretResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value);
}
