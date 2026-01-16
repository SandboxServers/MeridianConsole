using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class RevokeRoleCommand
{
    public static async Task<int> ExecuteAsync(string roleId, string userId, string? orgId, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(userId))
        {
            return IdentityCommandHelpers.WriteError("user_id_required", "User ID is required.");
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

        var request = new RoleAssignmentRequest
        {
            UserId = userId
        };

        try
        {
            var response = await identityApi.RevokeRoleAsync(orgId, roleId, request, ct);
            IdentityCommandHelpers.WriteJson(response);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
