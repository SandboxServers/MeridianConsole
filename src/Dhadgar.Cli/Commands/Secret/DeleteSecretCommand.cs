using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class DeleteSecretCommand
{
    public static async Task<int> ExecuteAsync(string name, bool force, CancellationToken ct)
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
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Are you sure you want to delete secret '[cyan]{Markup.Escape(name)}[/]'?[/]",
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

        var secretsApi = factory.CreateSecretsClient();

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("red"))
                .StartAsync($"[dim]Deleting secret '{Markup.Escape(name)}'...[/]", async _ =>
                {
                    await secretsApi.DeleteSecretAsync(name, ct);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Secret '[cyan]{Markup.Escape(name)}[/]' deleted successfully.");
            return 0;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            AnsiConsole.MarkupLine($"[red]Secret '[cyan]{Markup.Escape(name)}[/]' not found.[/]");
            return 1;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            AnsiConsole.MarkupLine($"[red]Access denied.[/] You don't have permission to delete this secret.");
            AnsiConsole.MarkupLine("[dim]Required permission: secrets:delete:{name} or secrets:delete:* or secrets:*[/]");
            return 1;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to delete secret:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
