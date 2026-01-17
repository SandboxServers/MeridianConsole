using System.Globalization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class ListClaimsCommand
{
    public static async Task<int> ExecuteAsync(string memberId, string? orgId, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(memberId))
        {
            return IdentityCommandHelpers.WriteError("member_id_required", "Member ID is required.");
        }

        orgId ??= config.CurrentOrgId;
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return IdentityCommandHelpers.WriteError(
                "org_id_required",
                "Organization ID is required. Use --org or set a current org.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var response = await identityApi.GetMemberClaimsAsync(orgId, memberId, ct);

            if (response.Claims.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No custom claims found for this member.[/]");
                AnsiConsole.MarkupLine("[dim]Permissions are determined by the member's role.[/]");
                AnsiConsole.MarkupLine("[dim]Use [cyan]dhadgar identity members grant[/] to add custom permissions.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn("[bold]ID[/]")
                .AddColumn("[bold]Type[/]")
                .AddColumn("[bold]Permission[/]")
                .AddColumn("[bold]Expires[/]")
                .AddColumn("[bold]Created[/]");

            foreach (var claim in response.Claims)
            {
                var isGrant = string.Equals(claim.Type, "grant", StringComparison.OrdinalIgnoreCase);
                var typeColor = isGrant ? "green" : "red";
                var typeDisplay = isGrant ? "✓ Grant" : "✗ Deny";
                var expiresDisplay = claim.ExpiresAt.HasValue
                    ? claim.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : "[green]Never[/]";

                table.AddRow(
                    $"[dim]{claim.Id[..8]}...[/]",
                    $"[{typeColor}]{typeDisplay}[/]",
                    $"[cyan]{Markup.Escape(claim.Value)}[/]",
                    expiresDisplay,
                    $"[dim]{claim.CreatedAt:yyyy-MM-dd}[/]");
            }

            var panel = new Panel(table)
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Blue),
                Header = new PanelHeader($" Custom Claims for Member {memberId[..8]}... ", Justify.Left)
            };

            AnsiConsole.Write(panel);
            AnsiConsole.MarkupLine($"\n[dim]Total: {response.Claims.Count} claim(s)[/]");

            return 0;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            AnsiConsole.MarkupLine($"[red]Member not found.[/]");
            return 1;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
