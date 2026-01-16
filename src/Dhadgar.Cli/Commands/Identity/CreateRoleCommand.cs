using System.Collections.ObjectModel;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class CreateRoleCommand
{
    public static async Task<int> ExecuteAsync(
        string name,
        string orgId,
        string? description,
        string? permissions,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return IdentityCommandHelpers.WriteError("role_name_required", "Role name is required.");
        }

        if (string.IsNullOrWhiteSpace(orgId))
        {
            return IdentityCommandHelpers.WriteError("org_id_required", "Organization ID is required.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var permissionList = ParsePermissions(permissions);
        var request = new CreateRoleRequest
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Permissions = permissionList.Count == 0 ? null : new Collection<string>(permissionList)
        };

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var response = await identityApi.CreateRoleAsync(orgId, request, ct);
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
