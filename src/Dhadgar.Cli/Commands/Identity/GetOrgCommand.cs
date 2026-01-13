using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class GetOrgCommand
{
    public static async Task<int> ExecuteAsync(string orgId, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(orgId))
        {
            return IdentityCommandHelpers.WriteError("org_id_required", "Organization ID is required.");
        }

        using var client = IdentityCommandHelpers.CreateClient(config);
        var url = $"{config.EffectiveIdentityUrl.TrimEnd('/')}/organizations/{orgId}";
        var response = await client.GetAsync(url, ct);

        return await IdentityCommandHelpers.WriteJsonResponseAsync(response, ct);
    }
}
