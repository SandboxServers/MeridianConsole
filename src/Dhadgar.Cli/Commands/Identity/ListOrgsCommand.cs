using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class ListOrgsCommand
{
    public static async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        using var client = IdentityCommandHelpers.CreateClient(config);
        var url = $"{config.EffectiveIdentityUrl.TrimEnd('/')}/organizations";
        var response = await client.GetAsync(url, ct);

        return await IdentityCommandHelpers.WriteJsonResponseAsync(response, ct);
    }
}
