using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
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

        var secretsUrl = config.EffectiveSecretsUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading Key Vaults...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);
                var response = await client.GetAsync<VaultsResponse>(
                    $"{secretsUrl.TrimEnd('/')}/api/v1/keyvaults",
                    ct);

                if (response?.Vaults is null || response.Vaults.Count == 0)
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

        return 0;
    }

    private sealed record VaultsResponse(
        [property: JsonPropertyName("vaults")] List<VaultItem> Vaults);

    private sealed record VaultItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("vaultUri")] string VaultUri,
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("secretCount")] int SecretCount,
        [property: JsonPropertyName("enabled")] bool Enabled);
}
