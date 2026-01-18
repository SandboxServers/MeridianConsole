using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.KeyVault;

public sealed class DeleteVaultCommand
{
    public static async Task<int> ExecuteAsync(string vaultName, bool force, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        // Confirm deletion unless --force is specified
        if (!force)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Deleting a Key Vault will remove all secrets, keys, and certificates stored in it.");
            AnsiConsole.MarkupLine("[dim]If soft-delete is enabled, the vault can be recovered within the retention period.[/]");
            AnsiConsole.WriteLine();

            var confirm = AnsiConsole.Confirm(
                $"[yellow]Are you sure you want to delete vault '[cyan]{Markup.Escape(vaultName)}[/]'?[/]",
                defaultValue: false);

            if (!confirm)
            {
                AnsiConsole.MarkupLine("[dim]Deletion cancelled.[/]");
                return 0;
            }
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var keyVaultApi = factory.CreateKeyVaultClient();

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("red"))
                .StartAsync($"[dim]Deleting vault '{Markup.Escape(vaultName)}'...[/]", async _ =>
                {
                    await keyVaultApi.DeleteVaultAsync(vaultName, ct);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Vault '[cyan]{Markup.Escape(vaultName)}[/]' deleted successfully.");
            AnsiConsole.MarkupLine("[dim]If soft-delete was enabled, the vault can be recovered using the Azure portal.[/]");
            return 0;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            AnsiConsole.MarkupLine($"[red]Vault '[cyan]{Markup.Escape(vaultName)}[/]' not found.[/]");
            return 1;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            AnsiConsole.MarkupLine($"[red]Access denied.[/] You don't have permission to delete this vault.");
            AnsiConsole.MarkupLine("[dim]Required permission: keyvault:write[/]");
            return 1;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to delete vault:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
