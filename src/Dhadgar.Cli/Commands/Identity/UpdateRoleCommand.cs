using System.Collections.ObjectModel;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class UpdateRoleCommand
{
    public static async Task<int> ExecuteAsync(
        string roleId,
        string? orgId,
        string? name,
        string? description,
        string? permissions,
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

        if (string.IsNullOrWhiteSpace(name) &&
            description is null &&
            permissions is null)
        {
            return IdentityCommandHelpers.WriteError(
                "no_updates",
                "At least one update (--name, --description, or --permissions) is required.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var permissionList = ParsePermissions(permissions);
        var request = new UpdateRoleRequest
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Description = description, // Allow empty string to clear description
            Permissions = permissions is null ? null : new Collection<string>(permissionList)
        };

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var response = await identityApi.UpdateRoleAsync(orgId, roleId.Trim(), request, ct);
            IdentityCommandHelpers.WriteJson(response);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }

    private static List<string> ParsePermissions(string? permissions)
    {
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return new List<string>();
        }

        return permissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(permission => permission.Trim())
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
