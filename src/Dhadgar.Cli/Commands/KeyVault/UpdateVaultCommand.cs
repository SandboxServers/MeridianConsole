using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.KeyVault;

public sealed class UpdateVaultCommand
{
    public static async Task<int> ExecuteAsync(
        string vaultName,
        bool? enableSoftDelete,
        bool? enablePurgeProtection,
        int? softDeleteRetentionDays,
        string? sku,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        // Build update request with only provided values
        var updates = new List<string>();
        var request = new UpdateVaultRequest
        {
            EnableSoftDelete = enableSoftDelete,
            EnablePurgeProtection = enablePurgeProtection,
            SoftDeleteRetentionDays = softDeleteRetentionDays,
            Sku = sku
        };

        if (enableSoftDelete.HasValue)
            updates.Add($"Soft Delete: {(enableSoftDelete.Value ? "Enabled" : "Disabled")}");
        if (enablePurgeProtection.HasValue)
            updates.Add($"Purge Protection: {(enablePurgeProtection.Value ? "Enabled" : "Disabled")}");
        if (softDeleteRetentionDays.HasValue)
            updates.Add($"Retention: {softDeleteRetentionDays.Value} days");
        if (!string.IsNullOrWhiteSpace(sku))
            updates.Add($"SKU: {sku}");

        if (updates.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No updates specified.[/] Use flags to specify what to update:");
            AnsiConsole.MarkupLine("[dim]  --enable-soft-delete / --disable-soft-delete[/]");
            AnsiConsole.MarkupLine("[dim]  --enable-purge-protection / --disable-purge-protection[/]");
            AnsiConsole.MarkupLine("[dim]  --retention-days <days>[/]");
            AnsiConsole.MarkupLine("[dim]  --sku <standard|premium>[/]");
            return 1;
        }

        // Show confirmation
        var panel = new Panel(new Markup(string.Join("\n", updates.Select(u => $"• {u}"))))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow),
            Header = new PanelHeader($" Updating Key Vault: {vaultName} ", Justify.Left)
        };

        AnsiConsole.Write(panel);

        var confirm = AnsiConsole.Confirm("\n[yellow]Apply these changes?[/]");
        if (!confirm)
        {
            AnsiConsole.MarkupLine("[dim]Update cancelled.[/]");
            return 0;
        }

        var secretsUrl = config.EffectiveSecretsUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Updating Key Vault '{vaultName}'...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);

                try
                {
                    var response = await client.PatchAsync<UpdateVaultRequest, UpdateVaultResponse>(
                        new Uri($"{secretsUrl.TrimEnd('/')}/api/v1/keyvaults/{vaultName}"),
                        request,
                        ct);

                    if (response != null)
                    {
                        var grid = new Grid()
                            .AddColumn()
                            .AddColumn();

                        grid.AddRow("[bold]Name:[/]", $"[cyan]{response.Name}[/]");
                        grid.AddRow("[bold]Soft Delete:[/]", FormatBool(response.EnableSoftDelete));
                        grid.AddRow("[bold]Purge Protection:[/]", FormatBool(response.EnablePurgeProtection));
                        grid.AddRow("[bold]Retention Days:[/]", $"[dim]{response.SoftDeleteRetentionDays}[/]");
                        grid.AddRow("[bold]SKU:[/]", $"[dim]{response.Sku}[/]");
                        grid.AddRow("[bold]Updated:[/]", $"[dim]{DateTime.UtcNow:g} UTC[/]");

                        var resultPanel = new Panel(grid)
                        {
                            Border = BoxBorder.Rounded,
                            BorderStyle = new Style(Color.Green),
                            Padding = new Padding(2, 1),
                            Header = new PanelHeader(" Vault Updated Successfully ", Justify.Left)
                        };

                        AnsiConsole.Write(resultPanel);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Vault may have been updated but no confirmation received");
                    }
                }
                catch (HttpRequestException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to update Key Vault:[/] {ex.Message}");

                    if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        AnsiConsole.MarkupLine($"[dim]Vault '{vaultName}' not found[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        AnsiConsole.MarkupLine($"[dim]You may not have permission to modify Key Vaults[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        AnsiConsole.MarkupLine($"[dim]Invalid configuration. Note: Purge protection cannot be disabled once enabled.[/]");
                    }
                }
            });

        return 0;
    }

    private static string FormatBool(bool value) =>
        value ? "[green]Enabled[/]" : "[dim]Disabled[/]";

    public sealed record UpdateVaultRequest
    {
        [JsonPropertyName("enableSoftDelete")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnableSoftDelete { get; init; }

        [JsonPropertyName("enablePurgeProtection")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnablePurgeProtection { get; init; }

        [JsonPropertyName("softDeleteRetentionDays")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SoftDeleteRetentionDays { get; init; }

        [JsonPropertyName("sku")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sku { get; init; }
    }

    public sealed record UpdateVaultResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("enableSoftDelete")] bool EnableSoftDelete,
        [property: JsonPropertyName("enablePurgeProtection")] bool EnablePurgeProtection,
        [property: JsonPropertyName("softDeleteRetentionDays")] int SoftDeleteRetentionDays,
        [property: JsonPropertyName("sku")] string Sku);
}
