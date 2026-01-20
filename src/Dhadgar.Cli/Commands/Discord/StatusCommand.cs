using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Discord;

public sealed class StatusCommand
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

        var api = factory.CreateDiscordClient();

        try
        {
            AnsiConsole.MarkupLine("[bold]Discord Bot Status[/]\n");

            var health = await api.GetHealthAsync(ct);

            var statusColor = health.BotStatus switch
            {
                "Connected" => "green",
                "Connecting" => "yellow",
                "Disconnected" => "red",
                _ => "dim"
            };

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .HideHeaders()
                .AddColumn("")
                .AddColumn("");

            table.AddRow("[cyan]Service[/]", Markup.Escape(health.Service));
            table.AddRow("[cyan]Status[/]", $"[{(health.Status == "ok" ? "green" : "red")}]{Markup.Escape(health.Status)}[/]");
            table.AddRow("[cyan]Bot Connection[/]", $"[{statusColor}]{Markup.Escape(health.BotStatus)}[/]");

            AnsiConsole.Write(table);

            // If admin API key is configured, also show platform health
            if (!string.IsNullOrEmpty(config.AdminApiKey))
            {
                AnsiConsole.MarkupLine("\n[bold]Platform Health (via Discord service)[/]\n");

                try
                {
                    var platformHealth = await api.GetPlatformHealthAsync(config.AdminApiKey, ct);

                    var platformTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Blue)
                        .AddColumn("[bold]Service[/]")
                        .AddColumn("[bold]Status[/]")
                        .AddColumn("[bold]Response Time[/]");

                    foreach (var service in platformHealth.Services)
                    {
                        var serviceStatusColor = service.IsHealthy ? "green" : "red";
                        var responseTime = service.ResponseTimeMs < 100
                            ? $"[green]{service.ResponseTimeMs}ms[/]"
                            : service.ResponseTimeMs < 500
                                ? $"[yellow]{service.ResponseTimeMs}ms[/]"
                                : $"[red]{service.ResponseTimeMs}ms[/]";

                        platformTable.AddRow(
                            $"[cyan]{Markup.Escape(service.Name)}[/]",
                            $"[{serviceStatusColor}]{(service.IsHealthy ? "Healthy" : "Unhealthy")}[/]",
                            responseTime);
                    }

                    AnsiConsole.Write(platformTable);
                    AnsiConsole.MarkupLine($"\n[dim]Healthy: {platformHealth.HealthyCount}, Unhealthy: {platformHealth.UnhealthyCount}[/]");
                    AnsiConsole.MarkupLine($"[dim]Checked at: {platformHealth.CheckedAtUtc:yyyy-MM-dd HH:mm:ss} UTC[/]");
                }
                catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    AnsiConsole.MarkupLine("[yellow]Platform health check requires valid admin API key[/]");
                }
            }

            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]API error: {(int)ex.StatusCode} {ex.StatusCode}[/]");
            if (!string.IsNullOrEmpty(ex.Content))
            {
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(ex.Content)}[/]");
            }
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
