using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Discord;

public sealed class ChannelsCommand
{
    public static async Task<int> ExecuteAsync(ulong? guildId, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (string.IsNullOrEmpty(config.AdminApiKey))
        {
            AnsiConsole.MarkupLine("[red]Admin API key required. Set 'admin_api_key' in config or DHADGAR_ADMIN_API_KEY env var.[/]");
            return 1;
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var api = factory.CreateDiscordClient();

        try
        {
            var response = await api.GetChannelsAsync(config.AdminApiKey, guildId, ct);

            if (!response.connected)
            {
                AnsiConsole.MarkupLine("[yellow]Bot is not connected to Discord[/]");
                if (!string.IsNullOrEmpty(response.message))
                {
                    AnsiConsole.MarkupLine($"[dim]{Markup.Escape(response.message)}[/]");
                }
                return 0;
            }

            if (response.guilds.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Bot is not a member of any guilds[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]Discord Channels[/] [dim]({response.guildCount} guild(s))[/]\n");

            foreach (var guild in response.guilds)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Blue)
                    .Title($"[cyan]{Markup.Escape(guild.guildName)}[/] [dim](ID: {guild.guildId})[/]")
                    .AddColumn("[bold]Channel[/]")
                    .AddColumn("[bold]Category[/]")
                    .AddColumn("[bold]ID[/]");

                foreach (var channel in guild.channels)
                {
                    table.AddRow(
                        $"[white]#{Markup.Escape(channel.name)}[/]",
                        channel.category is not null ? $"[dim]{Markup.Escape(channel.category)}[/]" : "[dim]-[/]",
                        $"[dim]{channel.channelId}[/]");
                }

                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();
            }

            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]API error: {(int)ex.StatusCode} {ex.StatusCode}[/]");
            if (!string.IsNullOrEmpty(ex.Content))
            {
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(ex.Content)}[/]");
            }
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
