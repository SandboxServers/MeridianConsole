using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Dhadgar.Contracts.Notifications;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Notifications;

public sealed class SendTestCommand
{
    public static async Task<int> ExecuteAsync(
        string? title,
        string? message,
        string? severity,
        Guid? orgId,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (string.IsNullOrEmpty(config.AdminApiKey))
        {
            AnsiConsole.MarkupLine("[red]Admin API key required. Set 'admin_api_key' in config or DHADGAR_ADMIN_API_KEY env var.[/]");
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
            var request = new TestNotificationRequest(
                OrgId: orgId,
                Title: title,
                Message: message,
                Severity: severity);

            var response = await api.SendTestNotificationAsync(config.AdminApiKey, request, ct);

            AnsiConsole.MarkupLine("[green]Test notification sent successfully![/]");
            AnsiConsole.MarkupLine($"[dim]Notification ID: {response.notificationId}[/]");

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
