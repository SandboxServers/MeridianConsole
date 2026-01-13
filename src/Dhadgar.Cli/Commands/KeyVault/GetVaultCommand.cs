using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.KeyVault;

public sealed class GetVaultCommand
{
    public static async Task<int> ExecuteAsync(string vaultName, CancellationToken ct)
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
            .StartAsync($"[dim]Loading Key Vault details...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);

                try
                {
                    var response = await client.GetAsync<VaultDetailResponse>(
                        new Uri($"{secretsUrl.TrimEnd('/')}/api/v1/keyvaults/{vaultName}"),
                        ct);

                    if (response == null)
                    {
                        AnsiConsole.MarkupLine($"[red]Vault '{vaultName}' not found[/]");
                        return;
                    }

                    // Main properties grid
                    var propsGrid = new Grid()
                        .AddColumn()
                        .AddColumn();

                    propsGrid.AddRow("[bold]Name:[/]", $"[cyan]{response.Name}[/]");
                    propsGrid.AddRow("[bold]Vault URI:[/]", $"[dim]{response.VaultUri}[/]");
                    propsGrid.AddRow("[bold]Location:[/]", $"[dim]{response.Location}[/]");
                    propsGrid.AddRow("[bold]Resource Group:[/]", $"[dim]{response.ResourceGroup}[/]");
                    propsGrid.AddRow("[bold]SKU:[/]", $"[dim]{response.Sku}[/]");
                    propsGrid.AddRow("[bold]Tenant ID:[/]", $"[dim]{response.TenantId}[/]");

                    var propsPanel = new Panel(propsGrid)
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Magenta1),
                        Header = new PanelHeader(" Properties ", Justify.Left)
                    };

                    // Security features grid
                    var securityGrid = new Grid()
                        .AddColumn()
                        .AddColumn();

                    securityGrid.AddRow("[bold]Soft Delete:[/]", FormatBool(response.EnableSoftDelete));
                    securityGrid.AddRow("[bold]Purge Protection:[/]", FormatBool(response.EnablePurgeProtection));
                    securityGrid.AddRow("[bold]Retention Days:[/]", $"[dim]{response.SoftDeleteRetentionDays} days[/]");
                    securityGrid.AddRow("[bold]RBAC Authorization:[/]", FormatBool(response.EnableRbacAuthorization));
                    securityGrid.AddRow("[bold]Public Network Access:[/]", FormatNetworkAccess(response.PublicNetworkAccess));

                    var securityPanel = new Panel(securityGrid)
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Yellow),
                        Header = new PanelHeader(" Security Configuration ", Justify.Left)
                    };

                    // Statistics grid
                    var statsGrid = new Grid()
                        .AddColumn()
                        .AddColumn();

                    statsGrid.AddRow("[bold]Secrets:[/]", $"[cyan]{response.SecretCount}[/]");
                    statsGrid.AddRow("[bold]Keys:[/]", $"[cyan]{response.KeyCount}[/]");
                    statsGrid.AddRow("[bold]Certificates:[/]", $"[cyan]{response.CertificateCount}[/]");
                    statsGrid.AddRow("[bold]Created:[/]", $"[dim]{response.CreatedAt:g} UTC[/]");
                    statsGrid.AddRow("[bold]Updated:[/]", $"[dim]{response.UpdatedAt:g} UTC[/]");

                    var statsPanel = new Panel(statsGrid)
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Green),
                        Header = new PanelHeader(" Statistics ", Justify.Left)
                    };

                    // Layout
                    var layout = new Layout("Root")
                        .SplitRows(
                            new Layout("Props", propsPanel),
                            new Layout("Bottom").SplitColumns(
                                new Layout("Security", securityPanel),
                                new Layout("Stats", statsPanel)
                            )
                        );

                    AnsiConsole.Write(layout);

                    // Warnings
                    if (!response.EnableSoftDelete)
                    {
                        AnsiConsole.MarkupLine("\n[red]⚠[/] [yellow]Soft Delete is disabled. Deleted secrets cannot be recovered.[/]");
                    }
                    if (!response.EnablePurgeProtection)
                    {
                        AnsiConsole.MarkupLine("[red]⚠[/] [yellow]Purge Protection is disabled. Soft-deleted items can be permanently purged.[/]");
                    }
                }
                catch (HttpRequestException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to retrieve vault details:[/] {ex.Message}");

                    if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        AnsiConsole.MarkupLine($"[dim]Vault '{vaultName}' not found[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        AnsiConsole.MarkupLine($"[dim]You may not have permission to view this vault[/]");
                    }
                }
            });

        return 0;
    }

    private static string FormatBool(bool value) =>
        value ? "[green]● Enabled[/]" : "[dim]○ Disabled[/]";

    private static string FormatNetworkAccess(string access) =>
        access.Equals("enabled", StringComparison.OrdinalIgnoreCase)
            ? "[green]Enabled[/]"
            : "[red]Disabled[/]";

    public sealed record VaultDetailResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("vaultUri")] Uri VaultUri,
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("resourceGroup")] string ResourceGroup,
        [property: JsonPropertyName("sku")] string Sku,
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("enableSoftDelete")] bool EnableSoftDelete,
        [property: JsonPropertyName("enablePurgeProtection")] bool EnablePurgeProtection,
        [property: JsonPropertyName("softDeleteRetentionDays")] int SoftDeleteRetentionDays,
        [property: JsonPropertyName("enableRbacAuthorization")] bool EnableRbacAuthorization,
        [property: JsonPropertyName("publicNetworkAccess")] string PublicNetworkAccess,
        [property: JsonPropertyName("secretCount")] int SecretCount,
        [property: JsonPropertyName("keyCount")] int KeyCount,
        [property: JsonPropertyName("certificateCount")] int CertificateCount,
        [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
        [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt);
}
