using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class SetSecretCommand
{
    public static async Task<int> ExecuteAsync(string secretName, string? value, bool stdin, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        // Get value from stdin if requested
        if (stdin)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] [dim]--stdin flag provided, ignoring value argument[/]");
            }

            AnsiConsole.MarkupLine("[dim]Reading secret value from stdin...[/]");
            value = (await Console.In.ReadToEndAsync(ct)).TrimEnd();

            if (string.IsNullOrWhiteSpace(value))
            {
                AnsiConsole.MarkupLine("[red]No input provided via stdin[/]");
                return 1;
            }
        }

        // Prompt for value if not provided
        if (string.IsNullOrWhiteSpace(value))
        {
            value = AnsiConsole.Prompt(
                new TextPrompt<string>($"[cyan]Enter value for '{secretName}':[/]")
                    .Secret()
                    .PromptStyle("green"));
        }

        // Azure Key Vault secret size limit is 25KB
        var valueBytes = System.Text.Encoding.UTF8.GetByteCount(value);
        const int maxSizeBytes = 25 * 1024; // 25KB

        if (valueBytes > maxSizeBytes)
        {
            AnsiConsole.MarkupLine($"[red]Secret value exceeds Azure Key Vault limit:[/]");
            AnsiConsole.MarkupLine($"[dim]Size: {valueBytes:N0} bytes (max: {maxSizeBytes:N0} bytes)[/]");
            AnsiConsole.MarkupLine($"[dim]For values larger than 25KB, consider storing in Azure Blob Storage and referencing the URL[/]");
            return 1;
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Setting secret '{secretName}'...[/]", async ctx =>
            {
                using var factory = new ApiClientFactory(config);
                var secretsApi = factory.CreateSecretsClient();

                try
                {
                    var response = await secretsApi.SetSecretAsync(
                        secretName,
                        new SetSecretRequest { Value = value },
                        ct);

                    if (response != null)
                    {
                        var panel = new Panel(new Markup(
                            $"[green]✓[/] Secret '[cyan]{secretName}[/]' updated successfully\n" +
                            $"[dim]Updated at:[/] {DateTime.UtcNow:g} UTC"))
                        {
                            Border = BoxBorder.Rounded,
                            BorderStyle = new Style(Color.Green),
                            Padding = new Padding(2, 1)
                        };

                        AnsiConsole.Write(panel);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Secret may have been updated but no confirmation received");
                    }
                }
                catch (ApiException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to set secret:[/] {ex.Message}");

                    if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        AnsiConsole.MarkupLine($"[dim]You may not have permission to modify '{secretName}'[/]");
                    }
                }
            });

        return 0;
    }

}
