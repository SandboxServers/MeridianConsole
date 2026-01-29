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

        if (identityUrl is null)
        {
            if (!Uri.TryCreate(config.EffectiveIdentityUrl, UriKind.Absolute, out var parsedIdentityUrl))
            {
                AnsiConsole.MarkupLine($"[red]Invalid Identity URL:[/] {Markup.Escape(config.EffectiveIdentityUrl)}");
                return 1;
            }

            identityUrl = parsedIdentityUrl;
        }

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
        using var factory = ApiClientFactory.TryCreate(config, null, identityUrl, null, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var identityApi = factory.CreateIdentityClient();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("[dim]Authenticating...[/]", async ctx =>
            {
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
                        AnsiConsole.MarkupLine($"[red]Authentication failed:[/] {(int)ex.StatusCode} {ex.StatusCode}");
                        WriteSafeApiErrorDetails(ex);
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

    private static void WriteSafeApiErrorDetails(ApiException ex)
    {
        if (string.IsNullOrWhiteSpace(ex.Content))
        {
            return;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(ex.Content);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return;
            }

            string? error = null;
            string? description = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("error"))
                {
                    error = property.Value.ToString();
                }
                else if (property.NameEquals("error_description"))
                {
                    description = property.Value.ToString();
                }
            }

            if (!string.IsNullOrWhiteSpace(error) || !string.IsNullOrWhiteSpace(description))
            {
                var detail = string.IsNullOrWhiteSpace(description)
                    ? error ?? "Unknown error"
                    : $"{error ?? "Error"}: {description}";
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(detail)}[/]");
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore malformed error payloads to avoid leaking raw content.
        }
    }

}
