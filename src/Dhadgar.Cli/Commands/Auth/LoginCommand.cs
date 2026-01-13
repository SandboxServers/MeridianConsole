using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Auth;

public sealed class LoginCommand
{
    public static async Task<int> ExecuteAsync(
        string? clientId,
        string? clientSecret,
        Uri? identityUrl,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        identityUrl ??= new Uri(config.EffectiveIdentityUrl);

        AnsiConsole.Write(
            new FigletText("Dhadgar")
                .LeftJustified()
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[dim]Meridian Console CLI[/]\n");

        // Interactive prompts if not provided
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Client ID:[/]")
                    .DefaultValue("dev-client")
                    .PromptStyle("green"));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            clientSecret = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Client Secret:[/]")
                    .DefaultValue("dev-secret")
                    .Secret()
                    .PromptStyle("green"));
        }

        // Request token using client credentials flow
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Authenticating...[/]", async ctx =>
            {
                using var factory = new ApiClientFactory(
                    gatewayUrl: new Uri(config.EffectiveGatewayUrl),
                    identityUrl: identityUrl);
                var identityApi = factory.CreateIdentityClient();

                var request = new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = "openid profile email servers:read servers:write nodes:manage"
                };

                try
                {
                    var tokenResponse = await identityApi.GetTokenAsync(request, ct);

                    if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                    {
                        AnsiConsole.MarkupLine("[red]Failed to parse token response[/]");
                        return;
                    }

                    config.AccessToken = tokenResponse.AccessToken;
                    config.RefreshToken = tokenResponse.RefreshToken;
                    config.IdentityUrl = identityUrl.ToString().TrimEnd('/');
                    config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
                    config.Save();

                    ctx.Status("[green]Authentication successful![/]");
                }
                catch (ApiException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Authentication failed:[/] {ex.Message}");
                    if (!string.IsNullOrWhiteSpace(ex.Content))
                    {
                        AnsiConsole.MarkupLine($"[dim]{ex.Content}[/]");
                    }
                }
            });

        if (config.IsAuthenticated())
        {
            var panel = new Panel(new Markup(
                $"[green]✓[/] Authenticated successfully\n" +
                $"[dim]Token expires:[/] {config.TokenExpiresAt:g}"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green),
                Padding = new Padding(2, 1)
            };

            AnsiConsole.Write(panel);
            return 0;
        }

        return 1;
    }

}
