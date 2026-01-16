using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Gateway;

public sealed class ServicesCommand
{
    public static async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var gateway = factory.CreateGatewayClient();

        try
        {
            await AnsiConsole.Status()
                .StartAsync("Checking all services...", async ctx =>
                {
                    var response = await gateway.GetAllServicesHealthAsync(ct);

                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Blue)
                        .AddColumn("[bold]Service[/]")
                        .AddColumn("[bold]Status[/]")
                        .AddColumn("[bold]Response Time[/]")
                        .AddColumn("[bold]URL[/]");

                    foreach (var (name, health) in response.Services.OrderBy(s => s.Key))
                    {
                        var status = health.IsHealthy
                            ? "[green]Healthy[/]"
                            : $"[red]{health.Error ?? "Unhealthy"}[/]";

                        var responseTime = health.ResponseTimeMs < 100
                            ? $"[green]{health.ResponseTimeMs}ms[/]"
                            : health.ResponseTimeMs < 500
                                ? $"[yellow]{health.ResponseTimeMs}ms[/]"
                                : $"[red]{health.ResponseTimeMs}ms[/]";

                        table.AddRow(
                            $"[cyan]{name}[/]",
                            status,
                            responseTime,
                            $"[dim]{health.Url}[/]");
                    }

                    AnsiConsole.Write(table);
                    AnsiConsole.MarkupLine($"\n[dim]{response.Summary}[/]");
                });

            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]API error: {ex.StatusCode} - {ex.Message}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
