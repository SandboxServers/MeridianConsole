using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Gateway;

public sealed class ClustersCommand
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
            var response = await gateway.GetClustersAsync(ct);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn("[bold]Cluster[/]")
                .AddColumn("[bold]Status[/]")
                .AddColumn("[bold]Available[/]")
                .AddColumn("[bold]Total[/]");

            foreach (var cluster in response.Clusters.OrderBy(c => c.ClusterId))
            {
                var status = cluster.HealthStatus == "Healthy"
                    ? "[green]Healthy[/]"
                    : $"[red]{Markup.Escape(cluster.HealthStatus)}[/]";

                var available = cluster.AvailableDestinations == cluster.TotalDestinations
                    ? $"[green]{cluster.AvailableDestinations}[/]"
                    : $"[yellow]{cluster.AvailableDestinations}[/]";

                table.AddRow(
                    $"[cyan]{Markup.Escape(cluster.ClusterId)}[/]",
                    status,
                    available,
                    cluster.TotalDestinations.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[dim]Total clusters: {response.Clusters.Count}[/]");

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
