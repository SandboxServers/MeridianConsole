using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.KeyVault;

public sealed class CreateVaultCommand
{
    public static async Task<int> ExecuteAsync(string? name, string? location, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        // Interactive prompts if not provided
        if (string.IsNullOrWhiteSpace(name))
        {
            name = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Key Vault name:[/]")
                    .PromptStyle("green")
                    .ValidationErrorMessage("[red]Vault name is required[/]")
                    .Validate(n =>
                    {
                        if (string.IsNullOrWhiteSpace(n)) return ValidationResult.Error("[red]Name cannot be empty[/]");
                        if (n.Length < 3 || n.Length > 24) return ValidationResult.Error("[red]Name must be 3-24 characters[/]");
                        if (!System.Text.RegularExpressions.Regex.IsMatch(n, "^[a-zA-Z0-9-]+$"))
                            return ValidationResult.Error("[red]Name can only contain letters, numbers, and hyphens[/]");
                        return ValidationResult.Success();
                    }));
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            location = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Select Azure location:[/]")
                    .PageSize(10)
                    .AddChoices(new[]
                    {
                        "centralus",
                        "eastus",
                        "eastus2",
                        "westus",
                        "westus2",
                        "westus3",
                        "northcentralus",
                        "southcentralus",
                        "westcentralus",
                        "northeurope",
                        "westeurope"
                    }));
        }

        var exitCode = 0;

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var keyVaultApi = factory.CreateKeyVaultClient();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Creating Key Vault '{name}'...[/]", async ctx =>
            {

                try
                {
                    var response = await keyVaultApi.CreateVaultAsync(
                        new CreateVaultRequest { Name = name, Location = location },
                        ct);

                    if (response is null)
                    {
                        AnsiConsole.MarkupLine("\n[red]Failed to create Key Vault[/]");
                        exitCode = 1;
                        return;
                    }

                    var grid = new Grid()
                        .AddColumn()
                        .AddColumn();

                    grid.AddRow("[bold]Name:[/]", $"[cyan]{response.Name}[/]");
                    grid.AddRow("[bold]URI:[/]", $"[dim]{response.VaultUri}[/]");
                    grid.AddRow("[bold]Location:[/]", $"[dim]{response.Location}[/]");
                    grid.AddRow("[bold]Resource Group:[/]", $"[dim]{response.ResourceGroup}[/]");
                    grid.AddRow("[bold]Created:[/]", $"[dim]{response.CreatedAt:g} UTC[/]");

                    var panel = new Panel(grid)
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Green),
                        Padding = new Padding(2, 1),
                        Header = new PanelHeader(" Key Vault Created ", Justify.Left)
                    };

                    AnsiConsole.Write(panel);

                    AnsiConsole.MarkupLine($"\n[dim]It may take a few minutes for the vault to be fully provisioned.[/]");
                    AnsiConsole.MarkupLine($"[dim]Use [cyan]dhadgar keyvault list[/] to view all vaults[/]");
                }
                catch (ApiException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to create Key Vault:[/] {ex.Message}");
                    exitCode = 1;

                    if (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        AnsiConsole.MarkupLine($"[dim]A vault with name '{name}' already exists[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        AnsiConsole.MarkupLine($"[dim]You may not have permission to create Key Vaults[/]");
                    }
                }
            });

        return exitCode;
    }

}
