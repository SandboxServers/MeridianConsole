using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class RotateSecretCommand
{
    public static async Task<int> ExecuteAsync(string secretName, bool force, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        if (!force)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Are you sure you want to rotate secret '[cyan]{secretName}[/cyan]'?[/]\n" +
                $"[dim]This will generate a new value and invalidate the old one.[/]");

            if (!confirm)
            {
                AnsiConsole.MarkupLine("[dim]Rotation cancelled.[/]");
                return 0;
            }
        }

        var secretsUrl = config.EffectiveSecretsUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Rotating secret '{secretName}'...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);

                try
                {
                    var response = await client.PostAsync<RotateRequest, RotateResponse>(
                        $"{secretsUrl.TrimEnd('/')}/api/v1/secrets/{secretName}/rotate",
                        new RotateRequest(),
                        ct);

                    if (response != null)
                    {
                        var grid = new Grid()
                            .AddColumn()
                            .AddColumn();

                        grid.AddRow("[bold]Secret Name:[/]", $"[cyan]{response.Name}[/]");
                        grid.AddRow("[bold]New Version:[/]", $"[dim]{response.Version}[/]");
                        grid.AddRow("[bold]Rotated At:[/]", $"[dim]{response.RotatedAt:g} UTC[/]");

                        if (response.ExpiresAt.HasValue)
                        {
                            grid.AddRow("[bold]Old Version Expires:[/]", $"[yellow]{response.ExpiresAt.Value:g} UTC[/]");
                        }

                        var panel = new Panel(grid)
                        {
                            Border = BoxBorder.Rounded,
                            BorderStyle = new Style(Color.Green),
                            Padding = new Padding(2, 1),
                            Header = new PanelHeader(" Secret Rotated Successfully ", Justify.Left)
                        };

                        AnsiConsole.Write(panel);

                        if (response.ExpiresAt.HasValue)
                        {
                            AnsiConsole.MarkupLine($"\n[yellow]⚠[/] [dim]The old secret version will remain valid until {response.ExpiresAt.Value:g} UTC[/]");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Rotation may have occurred but no confirmation received");
                    }
                }
                catch (HttpRequestException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to rotate secret:[/] {ex.Message}");

                    if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        AnsiConsole.MarkupLine($"[dim]You may not have permission to rotate '{secretName}'[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        AnsiConsole.MarkupLine($"[dim]Secret '{secretName}' does not exist or is not configured for rotation[/]");
                    }
                }
            });

        return 0;
    }

    private sealed record RotateRequest();

    private sealed record RotateResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("rotatedAt")] DateTime RotatedAt,
        [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt);
}
