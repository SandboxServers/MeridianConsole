using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Gateway;

public sealed class HealthCommand
{
    public static async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        var services = new[]
        {
            ("Gateway", $"{config.EffectiveGatewayUrl}/healthz"),
            ("Identity", $"{config.EffectiveIdentityUrl}/healthz"),
            ("Secrets", $"{config.EffectiveSecretsUrl}/healthz")
        };

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn("[bold]Service[/]")
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Response Time[/]")
            .AddColumn("[bold]Message[/]");

        await AnsiConsole.Live(table)
            .StartAsync(async ctx =>
            {
                foreach (var (serviceName, url) in services)
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

                    // Add authentication header if available
                    if (!string.IsNullOrWhiteSpace(config.AccessToken))
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AccessToken);
                    }

                    var sw = Stopwatch.StartNew();
                    string status;
                    string message = "";

                    try
                    {
                        var response = await client.GetAsync(url, ct);
                        sw.Stop();

                        if (response.IsSuccessStatusCode)
                        {
                            status = "[green]✓ Healthy[/]";

                            try
                            {
                                var healthResponse = await response.Content.ReadFromJsonAsync<HealthResponse>(ct);
                                message = healthResponse?.Status ?? "ok";
                            }
                            catch
                            {
                                message = "ok";
                            }
                        }
                        else
                        {
                            status = $"[yellow]⚠ {(int)response.StatusCode}[/]";
                            message = response.ReasonPhrase ?? "error";
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        sw.Stop();
                        status = "[red]✗ Timeout[/]";
                        message = "No response within 5s";
                    }
                    catch (HttpRequestException ex)
                    {
                        sw.Stop();
                        status = "[red]✗ Unreachable[/]";
                        message = ex.Message;
                    }

                    var responseTime = sw.ElapsedMilliseconds < 100
                        ? $"[green]{sw.ElapsedMilliseconds}ms[/]"
                        : sw.ElapsedMilliseconds < 500
                            ? $"[yellow]{sw.ElapsedMilliseconds}ms[/]"
                            : $"[red]{sw.ElapsedMilliseconds}ms[/]";

                    table.AddRow(
                        $"[cyan]{serviceName}[/]",
                        status,
                        responseTime,
                        $"[dim]{message}[/]");

                    ctx.Refresh();
                }
            });

        return 0;
    }

    public sealed record HealthResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("service")] string? Service);
}
