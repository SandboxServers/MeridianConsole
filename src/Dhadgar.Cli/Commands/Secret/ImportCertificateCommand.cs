using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class ImportCertificateCommand
{
    public static async Task<int> ExecuteAsync(
        string certPath,
        string? name,
        string? password,
        string? vaultName,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        // Verify file exists
        if (!File.Exists(certPath))
        {
            AnsiConsole.MarkupLine($"[red]Certificate file not found:[/] {certPath}");
            return 1;
        }

        var fileInfo = new FileInfo(certPath);
        var extension = fileInfo.Extension.ToLowerInvariant();

        if (extension != ".pfx" && extension != ".p12" && extension != ".pem" && extension != ".cer")
        {
            AnsiConsole.MarkupLine($"[red]Unsupported certificate format:[/] {extension}");
            AnsiConsole.MarkupLine("[dim]Supported formats: .pfx, .p12, .pem, .cer[/]");
            return 1;
        }

        // Default name from filename
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(certPath);
        }

        // Prompt for password if PFX/P12 and not provided
        if ((extension == ".pfx" || extension == ".p12") && string.IsNullOrWhiteSpace(password))
        {
            password = AnsiConsole.Prompt(
                new TextPrompt<string>($"[cyan]Certificate password (optional):[/]")
                    .Secret()
                    .AllowEmpty()
                    .PromptStyle("green"));
        }

        var secretsUrl = config.EffectiveSecretsUrl;
        var url = string.IsNullOrWhiteSpace(vaultName)
            ? $"{secretsUrl.TrimEnd('/')}/api/v1/certificates"
            : $"{secretsUrl.TrimEnd('/')}/api/v1/keyvaults/{vaultName}/certificates";

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[dim]Importing certificate '{name}'...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);

                try
                {
                    // Read certificate file as base64
                    var certBytes = await File.ReadAllBytesAsync(certPath, ct);
                    var certBase64 = Convert.ToBase64String(certBytes);

                    var response = await client.PostAsync<ImportCertRequest, ImportCertResponse>(
                        url,
                        new ImportCertRequest(name, certBase64, password),
                        ct);

                    if (response != null)
                    {
                        var grid = new Grid()
                            .AddColumn()
                            .AddColumn();

                        grid.AddRow("[bold]Name:[/]", $"[cyan]{response.Name}[/]");
                        grid.AddRow("[bold]Subject:[/]", $"[dim]{response.Subject}[/]");
                        grid.AddRow("[bold]Issuer:[/]", $"[dim]{response.Issuer}[/]");
                        grid.AddRow("[bold]Thumbprint:[/]", $"[dim]{response.Thumbprint}[/]");
                        grid.AddRow("[bold]Expires:[/]", $"[dim]{response.ExpiresAt:yyyy-MM-dd HH:mm} UTC[/]");
                        grid.AddRow("[bold]Imported:[/]", $"[dim]{DateTime.UtcNow:g} UTC[/]");

                        var expiresIn = response.ExpiresAt - DateTime.UtcNow;
                        if (expiresIn.TotalDays < 90)
                        {
                            var color = expiresIn.TotalDays < 30 ? "red" : "yellow";
                            grid.AddRow("[bold]Warning:[/]", $"[{color}]Expires in {(int)expiresIn.TotalDays} days[/]");
                        }

                        var panel = new Panel(grid)
                        {
                            Border = BoxBorder.Rounded,
                            BorderStyle = new Style(Color.Green),
                            Padding = new Padding(2, 1),
                            Header = new PanelHeader(" Certificate Imported ", Justify.Left)
                        };

                        AnsiConsole.Write(panel);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Certificate may have been imported but no confirmation received");
                    }
                }
                catch (HttpRequestException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to import certificate:[/] {ex.Message}");

                    if (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        AnsiConsole.MarkupLine($"[dim]The certificate may be invalid or the password incorrect[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        AnsiConsole.MarkupLine($"[dim]A certificate with name '{name}' already exists[/]");
                    }
                    else if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        AnsiConsole.MarkupLine($"[dim]You may not have permission to import certificates[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Error reading certificate file:[/] {ex.Message}");
                }
            });

        return 0;
    }

    private sealed record ImportCertRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("certificateData")] string CertificateData,
        [property: JsonPropertyName("password")] string? Password);

    private sealed record ImportCertResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("thumbprint")] string Thumbprint,
        [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt);
}
