using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class DeleteOrgCommand
{
    public static async Task<int> ExecuteAsync(string orgId, bool force, CancellationToken ct)
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

        if (!force && !ConfirmDelete(orgId))
        {
            IdentityCommandHelpers.WriteJson(new
            {
                deleted = false,
                organizationId = orgId,
                cancelled = true
            });
            return 0;
        }

        using var client = IdentityCommandHelpers.CreateClient(config);
        var url = $"{config.EffectiveIdentityUrl.TrimEnd('/')}/organizations/{orgId}";
        var response = await client.DeleteAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            IdentityCommandHelpers.WriteJson(new
            {
                deleted = true,
                organizationId = orgId
            });
            return 0;
        }

        return IdentityCommandHelpers.WriteHttpError(response, body);
    }

    private static bool ConfirmDelete(string orgId)
    {
        Console.Error.Write($"Delete organization {orgId}? [y/N]: ");
        var input = Console.ReadLine();
        return input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true ||
               input?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
    }
}
