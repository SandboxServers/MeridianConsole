using System.Diagnostics;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Gateway;

public sealed class HealthCommand
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

        var services = new[]
        {
            ("Gateway", factory.CreateGatewayHealthClient()),
            ("Identity", factory.CreateIdentityHealthClient()),
            ("Secrets", factory.CreateSecretsHealthClient())
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
                foreach (var (serviceName, healthApi) in services)
                {
                    var sw = Stopwatch.StartNew();
                    string status;
                    string message = "";

                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                        var response = await healthApi.GetHealthAsync(timeoutCts.Token);
                        sw.Stop();

                        status = "[green]û Healthy[/]";
                        message = response?.Status ?? "ok";
                    }
                    catch (TaskCanceledException)
                    {
                        sw.Stop();
                        status = "[red]? Timeout[/]";
                        message = "No response within 5s";
                    }
                    catch (ApiException ex)
                    {
                        sw.Stop();
                        status = $"[yellow]? {(int)ex.StatusCode}[/]";
                        message = ex.StatusCode.ToString();
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
}
