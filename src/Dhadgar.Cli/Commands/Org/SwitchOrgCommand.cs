using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Org;

public sealed class SwitchOrgCommand
{
    public static async Task<int> ExecuteAsync(string orgId, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run [cyan]dhadgar auth login[/] first.");
            return 1;
        }

        if (!Guid.TryParse(orgId, out var orgGuid))
        {
            AnsiConsole.MarkupLine("[red]Invalid organization ID format[/]");
            return 1;
        }

        var identityUrl = config.EffectiveIdentityUrl;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Switching organization...[/]", async ctx =>
            {
                using var client = new AuthenticatedHttpClient(config);

                var response = await client.PostAsync<object, SwitchResponse>(
                    new Uri($"{identityUrl.TrimEnd('/')}/organizations/{orgGuid}/switch"),
                    new { },
                    ct);

                if (response?.AccessToken is not null)
                {
                    config.AccessToken = response.AccessToken;
                    config.RefreshToken = response.RefreshToken;
                    config.CurrentOrgId = response.OrganizationId?.ToString();
                    config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn - 60);
                    config.Save();

                    var permissionsList = response.Permissions is not null && response.Permissions.Count > 0
                        ? string.Join(", ", response.Permissions.Select(p => $"[dim]{p}[/]"))
                        : "[dim]none[/]";

                    var panel = new Panel(new Markup(
                        $"[green]✓[/] Switched to organization\n\n" +
                        $"[bold]Organization ID:[/] [cyan]{response.OrganizationId}[/]\n" +
                        $"[bold]Token expires:[/] {config.TokenExpiresAt:g}\n" +
                        $"[bold]Permissions:[/] {permissionsList}"))
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Green),
                        Padding = new Padding(2, 1),
                        Header = new PanelHeader(" Organization Switched ", Justify.Left)
                    };

                    AnsiConsole.Write(panel);
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Failed to switch organization[/]");
                }
            });

        return 0;
    }

    public sealed record SwitchResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("refreshToken")] string? RefreshToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn,
        [property: JsonPropertyName("organizationId")] Guid? OrganizationId,
        [property: JsonPropertyName("permissions")] Collection<string>? Permissions);
}
