using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.KeyVault;

public sealed class ListVaultsCommand
{
    public static async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        var exitCode = 0;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading Key Vaults...[/]", async ctx =>
            {
                using var factory = new ApiClientFactory(config);
                var keyVaultApi = factory.CreateKeyVaultClient();

                KeyVaultListResponse response;
                try
                {
                    response = await keyVaultApi.GetVaultsAsync(ct);
                }
                catch (ApiException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to load Key Vaults:[/] {ex.Message}");
                    exitCode = 1;
                    return;
                }

                if (response?.Vaults is null)
                {
                    AnsiConsole.MarkupLine("\n[red]Failed to load Key Vault list[/]");
                    exitCode = 1;
                    return;
                }

                if (response.Vaults.Count == 0)
                {
                    AnsiConsole.MarkupLine("\n[yellow]No Key Vaults found.[/]");
                    AnsiConsole.MarkupLine("[dim]Use [cyan]dhadgar keyvault create[/] to create a new vault[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Magenta1)
                    .AddColumn("[bold]Name[/]")
                    .AddColumn("[bold]URI[/]")
                    .AddColumn("[bold]Location[/]")
                    .AddColumn("[bold]Secrets[/]")
                    .AddColumn("[bold]Status[/]");

                foreach (var vault in response.Vaults)
                {
                    var statusColor = vault.Enabled ? "green" : "dim";
                    var statusText = vault.Enabled ? "● Enabled" : "○ Disabled";

                    table.AddRow(
                        $"[cyan]{vault.Name}[/]",
                        $"[dim]{vault.VaultUri}[/]",
                        $"[dim]{vault.Location}[/]",
                        $"[dim]{vault.SecretCount} secret(s)[/]",
                        $"[{statusColor}]{statusText}[/]");
                }

                var panel = new Panel(table)
                {
                    Border = BoxBorder.Double,
                    BorderStyle = new Style(Color.Magenta1),
                    Header = new PanelHeader(" Azure Key Vaults ", Justify.Left)
                };

                AnsiConsole.Write(panel);
                AnsiConsole.MarkupLine($"\n[dim]Total: {response.Vaults.Count} vault(s)[/]");
            });

        return exitCode;
    }

}
