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

            var statusColor = health.botStatus switch
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

            table.AddRow("[cyan]Service[/]", Markup.Escape(health.service));
            table.AddRow("[cyan]Status[/]", $"[{(health.status == "ok" ? "green" : "red")}]{Markup.Escape(health.status)}[/]");
            table.AddRow("[cyan]Bot Connection[/]", $"[{statusColor}]{Markup.Escape(health.botStatus)}[/]");

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

                    foreach (var service in platformHealth.services)
                    {
                        var serviceStatusColor = service.isHealthy ? "green" : "red";
                        string responseTime;
                        if (service.responseTimeMs.HasValue)
                        {
                            var ms = service.responseTimeMs.Value;
                            responseTime = ms < 100
                                ? $"[green]{ms}ms[/]"
                                : ms < 500
                                    ? $"[yellow]{ms}ms[/]"
                                    : $"[red]{ms}ms[/]";
                        }
                        else
                        {
                            responseTime = "[dim]n/a[/]";
                        }

                        platformTable.AddRow(
                            $"[cyan]{Markup.Escape(service.name)}[/]",
                            $"[{serviceStatusColor}]{(service.isHealthy ? "Healthy" : "Unhealthy")}[/]",
                            responseTime);
                    }

                    AnsiConsole.Write(platformTable);
                    AnsiConsole.MarkupLine($"\n[dim]Healthy: {platformHealth.healthyCount}, Unhealthy: {platformHealth.unhealthyCount}[/]");
                    AnsiConsole.MarkupLine($"[dim]Checked at: {platformHealth.checkedAtUtc:yyyy-MM-dd HH:mm:ss} UTC[/]");
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
