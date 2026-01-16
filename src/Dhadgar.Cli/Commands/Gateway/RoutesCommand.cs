using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Gateway;

public sealed class RoutesCommand
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
            var response = await gateway.GetRoutesAsync(ct);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn("[bold]Route[/]")
                .AddColumn("[bold]Path[/]")
                .AddColumn("[bold]Cluster[/]")
                .AddColumn("[bold]Auth Policy[/]")
                .AddColumn("[bold]Rate Limit[/]")
                .AddColumn("[bold]Order[/]");

            foreach (var route in response.Routes.OrderBy(r => r.Order ?? 999).ThenBy(r => r.RouteId))
            {
                table.AddRow(
                    $"[cyan]{Markup.Escape(route.RouteId)}[/]",
                    Markup.Escape(route.Path),
                    $"[yellow]{Markup.Escape(route.ClusterId)}[/]",
                    route.AuthorizationPolicy is not null ? Markup.Escape(route.AuthorizationPolicy) : "[dim]default[/]",
                    route.RateLimiterPolicy is not null ? Markup.Escape(route.RateLimiterPolicy) : "[dim]none[/]",
                    route.Order?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "[dim]-[/]");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[dim]Total routes: {response.Routes.Count}[/]");

            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]API error: {ex.StatusCode} - {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
