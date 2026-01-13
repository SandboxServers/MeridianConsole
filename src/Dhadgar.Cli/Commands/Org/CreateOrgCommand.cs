using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Org;

public sealed class CreateOrgCommand
{
    public static async Task<int> ExecuteAsync(string? name, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Organization name:[/]")
                    .PromptStyle("green")
                    .ValidationErrorMessage("[red]Name cannot be empty[/]")
                    .Validate(n => !string.IsNullOrWhiteSpace(n)));
        }

        var identityUrl = config.EffectiveIdentityUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Creating organization '{name}'...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);

                var request = new CreateOrgRequest(name);
                var response = await client.PostAsync<CreateOrgRequest, CreateOrgResponse>(
                    new Uri($"{identityUrl.TrimEnd('/')}/organizations"),
                    request,
                    ct);

                if (response?.Id is not null)
                {
                    var panel = new Panel(new Markup(
                        $"[green]✓[/] Organization created successfully\n\n" +
                        $"[bold]ID:[/] [cyan]{response.Id}[/]\n" +
                        $"[bold]Name:[/] {name}"))
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Green),
                        Padding = new Padding(2, 1),
                        Header = new PanelHeader(" Organization Created ", Justify.Left)
                    };

                    AnsiConsole.Write(panel);

                    // Offer to switch to the new org
                    if (AnsiConsole.Confirm($"Switch to [cyan]{name}[/] as your active organization?", defaultValue: true))
                    {
                        config.CurrentOrgId = response.Id.ToString();
                        config.Save();
                        AnsiConsole.MarkupLine("[green]✓[/] Switched to new organization");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Failed to create organization[/]");
                }
            });

        return 0;
    }

    public sealed record CreateOrgRequest(
        [property: JsonPropertyName("name")] string Name);

    public sealed record CreateOrgResponse(
        [property: JsonPropertyName("id")] Guid? Id);
}
