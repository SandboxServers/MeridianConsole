using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class ListRoleMembersCommand
{
    public static async Task<int> ExecuteAsync(
        string roleId,
        string? orgId,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(roleId))
        {
            return IdentityCommandHelpers.WriteError("role_id_required", "Role ID is required.");
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
            var response = await identityApi.GetRoleMembersAsync(orgId, roleId.Trim(), ct);
            IdentityCommandHelpers.WriteJson(response);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
