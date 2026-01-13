using System.Globalization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Member;

public sealed class ListMembersCommand
{
    public static async Task<int> ExecuteAsync(string? orgId, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        orgId ??= config.CurrentOrgId;

        if (string.IsNullOrWhiteSpace(orgId))
        {
            AnsiConsole.MarkupLine("[yellow]No organization specified.[/]");
            AnsiConsole.MarkupLine("[dim]Use:[/] [cyan]dhadgar member list <org-id>[/]");
            AnsiConsole.MarkupLine("[dim]Or set current org with:[/] [cyan]dhadgar identity orgs switch <org-id>[/]");
            return 1;
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading members...[/]", async ctx =>
            {
                using var factory = new ApiClientFactory(config);
                var identityApi = factory.CreateIdentityClient();

                List<MemberResponse> members;
                try
                {
                    members = await identityApi.GetMembersAsync(orgId, ct);
                }
                catch (ApiException ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]Failed to load members:[/] {ex.Message}");
                    return;
                }

                if (members.Count == 0)
                {
                    AnsiConsole.MarkupLine("\n[yellow]No members found.[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Blue)
                    .AddColumn("[bold]ID[/]")
                    .AddColumn("[bold]Email[/]")
                    .AddColumn("[bold]Role[/]")
                    .AddColumn("[bold]Status[/]")
                    .AddColumn("[bold]Joined[/]");

                foreach (var member in members)
                {
                    var roleColor = member.Role?.ToLowerInvariant() switch
                    {
                        "owner" => "yellow",
                        "admin" => "blue",
                        "member" => "green",
                        _ => "dim"
                    };

                    var statusIcon = member.Status?.ToLowerInvariant() switch
                    {
                        "active" => "[green]✓[/]",
                        "pending" => "[yellow]⧗[/]",
                        "suspended" => "[red]✗[/]",
                        _ => "[dim]?[/]"
                    };

                    var joinedDate = member.JoinedAt.HasValue
                        ? member.JoinedAt.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : "[dim]n/a[/]";

                    var idDisplay = member.Id.Length > 8
                        ? $"{member.Id[..8]}..."
                        : member.Id;

                    table.AddRow(
                        $"[dim]{idDisplay}[/]",
                        string.IsNullOrWhiteSpace(member.Email) ? "[dim]unknown[/]" : member.Email,
                        $"[{roleColor}]{member.Role}[/]",
                        statusIcon,
                        joinedDate);
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total: {members.Count} member(s)[/]");
            });

        return 0;
    }

}
