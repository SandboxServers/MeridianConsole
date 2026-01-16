using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class UpdateOrgCommand
{
    public static async Task<int> ExecuteAsync(string orgId, string? name, string? description, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))
        {
            return IdentityCommandHelpers.WriteError(
                "missing_update_fields",
                "Provide --name and/or --description.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }
        var identityApi = factory.CreateIdentityClient();

        try
        {
            OrganizationSettingsUpdateRequest? settings = null;
            if (!string.IsNullOrWhiteSpace(description))
            {
                settings = new OrganizationSettingsUpdateRequest
                {
                    CustomSettings = new Dictionary<string, string>
                    {
                        ["description"] = description.Trim()
                    }
                };
            }

            var request = new UpdateOrganizationRequest
            {
                Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                Settings = settings
            };

            var updated = await identityApi.UpdateOrganizationAsync(orgId, request, ct);
            IdentityCommandHelpers.WriteJson(updated);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
