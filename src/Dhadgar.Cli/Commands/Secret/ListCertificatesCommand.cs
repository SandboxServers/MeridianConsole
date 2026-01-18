using System.Globalization;
using Dhadgar.Cli.Commands;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class ListCertificatesCommand
{
    public static async Task<int> ExecuteAsync(string? vaultName, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(vaultName) && !CommandValidation.TryValidateVaultName(vaultName))
        {
            return 1;
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var secretsApi = factory.CreateSecretsClient();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading certificates...[/]", async ctx =>
            {
                    CertificateListResponse response;
                    try
                    {
                        response = string.IsNullOrWhiteSpace(vaultName)
                            ? await secretsApi.GetCertificatesAsync(ct)
                            : await secretsApi.GetVaultCertificatesAsync(vaultName, ct);
                    }
                    catch (ApiException ex)
                    {
                        AnsiConsole.MarkupLine($"\n[red]Failed to load certificates:[/] {ex.Message}");
                        return;
                    }

                if (response.Certificates.Count == 0)
                {
                    AnsiConsole.MarkupLine("\n[yellow]No certificates found.[/]");
                    AnsiConsole.MarkupLine("[dim]Use [cyan]dhadgar secret import-cert[/] to add a certificate[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Magenta1)
                    .AddColumn("[bold]Name[/]")
                    .AddColumn("[bold]Subject[/]")
                    .AddColumn("[bold]Issuer[/]")
                    .AddColumn("[bold]Expires[/]")
                    .AddColumn("[bold]Status[/]");

                foreach (var cert in response.Certificates)
                {
                    var expiresIn = cert.ExpiresAt - DateTime.UtcNow;
                    var expiryColor = expiresIn.TotalDays < 30 ? "red" : expiresIn.TotalDays < 90 ? "yellow" : "green";
                    var expiryText = cert.ExpiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                    var statusIcon = cert.Enabled ? "●" : "○";
                    var statusColor = cert.Enabled ? "green" : "dim";

                    table.AddRow(
                        $"[cyan]{cert.Name}[/]",
                        $"[dim]{cert.Subject}[/]",
                        $"[dim]{TruncateIssuer(cert.Issuer)}[/]",
                        $"[{expiryColor}]{expiryText}[/]",
                        $"[{statusColor}]{statusIcon}[/]");
                }

                var header = string.IsNullOrWhiteSpace(vaultName)
                    ? " Certificates "
                    : $" Certificates in {vaultName} ";

                var panel = new Panel(table)
                {
                    Border = BoxBorder.Double,
                    BorderStyle = new Style(Color.Magenta1),
                    Header = new PanelHeader(header, Justify.Left)
                };

                AnsiConsole.Write(panel);

                var expiringSoon = response.Certificates.Count(c => (c.ExpiresAt - DateTime.UtcNow).TotalDays < 30);
                if (expiringSoon > 0)
                {
                    AnsiConsole.MarkupLine($"\n[red]⚠[/] [yellow]{expiringSoon} certificate(s) expiring within 30 days[/]");
                }

                AnsiConsole.MarkupLine($"[dim]Total: {response.Certificates.Count} certificate(s)[/]");
            });

        return 0;
    }

    private static string TruncateIssuer(string issuer)
    {
        // Extract CN from issuer DN
        var cnMatch = System.Text.RegularExpressions.Regex.Match(issuer, @"CN=([^,]+)");
        if (cnMatch.Success)
        {
            return cnMatch.Groups[1].Value;
        }
        return issuer.Length > 30 ? issuer[..27] + "..." : issuer;
    }

}
