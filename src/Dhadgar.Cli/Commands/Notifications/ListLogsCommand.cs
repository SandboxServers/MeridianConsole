using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Notifications;

public sealed class ListLogsCommand
{
    public static async Task<int> ExecuteAsync(
        int? limit,
        string? status,
        Guid? orgId,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (string.IsNullOrEmpty(config.AdminApiKey))
        {
            AnsiConsole.MarkupLine("[red]Admin API key not configured.[/]");
            AnsiConsole.MarkupLine("[dim]Set it in ~/.dhadgar/config.json as \"admin_api_key\"[/]");
            return 1;
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var api = factory.CreateNotificationsClient();

        try
        {
            var logs = await api.GetLogsAsync(
                config.AdminApiKey,
                limit,
                status,
                orgId,
                orgId?.ToString(),
                ct);

            if (logs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No notification logs found.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn("[bold]ID[/]")
                .AddColumn("[bold]Event Type[/]")
                .AddColumn("[bold]Channel[/]")
                .AddColumn("[bold]Recipient[/]")
                .AddColumn("[bold]Status[/]")
                .AddColumn("[bold]Created At[/]");

            foreach (var log in logs)
            {
                var statusColor = log.Status switch
                {
                    "sent" => "green",
                    "failed" => "red",
                    "pending" => "yellow",
                    _ => "dim"
                };

                table.AddRow(
                    $"[dim]{log.Id.ToString()[..8]}...[/]",
                    $"[cyan]{Markup.Escape(log.EventType)}[/]",
                    $"[blue]{Markup.Escape(log.Channel)}[/]",
                    Markup.Escape(TruncateString(log.Recipient, 30)),
                    $"[{statusColor}]{Markup.Escape(log.Status)}[/]",
                    $"[dim]{log.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}[/]");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[dim]Showing {logs.Count} notification log(s)[/]");

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

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}
