using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
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

        var secretsUrl = config.EffectiveSecretsUrl;
        var url = string.IsNullOrWhiteSpace(vaultName)
            ? $"{secretsUrl.TrimEnd('/')}/api/v1/certificates"
            : $"{secretsUrl.TrimEnd('/')}/api/v1/keyvaults/{vaultName}/certificates";

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading certificates...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);
                var response = await client.GetAsync<CertificatesResponse>(url, ct);

                if (response?.Certificates is null || response.Certificates.Count == 0)
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
                    var expiryText = cert.ExpiresAt.ToString("yyyy-MM-dd");

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

    private sealed record CertificatesResponse(
        [property: JsonPropertyName("certificates")] List<CertificateItem> Certificates);

    private sealed record CertificateItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt,
        [property: JsonPropertyName("thumbprint")] string Thumbprint,
        [property: JsonPropertyName("enabled")] bool Enabled);
}
