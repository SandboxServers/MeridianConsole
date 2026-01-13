using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

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

        using var factory = new ApiClientFactory(config);
        var identityApi = factory.CreateIdentityClient();

        try
        {
            await identityApi.DeleteOrganizationAsync(orgId, ct);

            IdentityCommandHelpers.WriteJson(new
            {
                deleted = true,
                organizationId = orgId
            });
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }

    private static bool ConfirmDelete(string orgId)
    {
        Console.Error.Write($"Delete organization {orgId}? [y/N]: ");
        var input = Console.ReadLine();
        return input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true ||
               input?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
    }
}
