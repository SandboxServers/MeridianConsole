using System.Globalization;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
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
            AnsiConsole.MarkupLine("[dim]Or set current org with:[/] [cyan]dhadgar org switch <org-id>[/]");
            return 1;
        }

        var identityUrl = config.EffectiveIdentityUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Loading members...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);
                var members = await client.GetAsync<List<MemberListItem>>(
                    new Uri($"{identityUrl.TrimEnd('/')}/organizations/{orgId}/members"),
                    ct);

                if (members is null || members.Count == 0)
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

                    table.AddRow(
                        $"[dim]{member.Id?.ToString()[..8]}...[/]",
                        member.Email ?? "[dim]unknown[/]",
                        $"[{roleColor}]{member.Role}[/]",
                        statusIcon,
                        joinedDate);
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total: {members.Count} member(s)[/]");
            });

        return 0;
    }

    public sealed record MemberListItem(
        [property: JsonPropertyName("id")] Guid? Id,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("joinedAt")] DateTime? JoinedAt);
}
