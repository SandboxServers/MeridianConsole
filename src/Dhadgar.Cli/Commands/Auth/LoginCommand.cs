using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Auth;

public sealed class LoginCommand
{
    public static async Task<int> ExecuteAsync(
        string? clientId,
        string? clientSecret,
        string? identityUrl,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        identityUrl ??= config.EffectiveIdentityUrl;

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
                using var client = new HttpClient();
                var tokenUrl = $"{identityUrl.TrimEnd('/')}/connect/token";

                var request = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = "openid profile email servers:read servers:write nodes:manage"
                });

                try
                {
                    var response = await client.PostAsync(tokenUrl, request, ct);
                    var content = await response.Content.ReadAsStringAsync(ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        AnsiConsole.MarkupLine($"[red]Authentication failed:[/] {response.StatusCode}");
                        AnsiConsole.MarkupLine($"[dim]{content}[/]");
                        return;
                    }

                    var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

                    if (tokenResponse?.AccessToken is null)
                    {
                        AnsiConsole.MarkupLine("[red]Failed to parse token response[/]");
                        return;
                    }

                    config.AccessToken = tokenResponse.AccessToken;
                    config.RefreshToken = tokenResponse.RefreshToken;
                    config.IdentityUrl = identityUrl;
                    config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
                    config.Save();

                    ctx.Status("[green]Authentication successful![/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
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

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
