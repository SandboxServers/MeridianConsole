using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class CreateOrgCommand
{
    public static async Task<int> ExecuteAsync(string name, string? description, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return IdentityCommandHelpers.WriteError("name_required", "Organization name is required.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var request = new CreateOrganizationRequest { Name = name.Trim() };

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var created = await identityApi.CreateOrganizationAsync(request, ct);

            if (string.IsNullOrWhiteSpace(description))
            {
                IdentityCommandHelpers.WriteJson(created);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(created.Id))
            {
                return IdentityCommandHelpers.WriteError("invalid_response", "Create response missing organization id.");
            }

            var updateRequest = new UpdateOrganizationRequest
            {
                Settings = new OrganizationSettingsUpdateRequest
                {
                    CustomSettings = new Dictionary<string, string>
                    {
                        ["description"] = description.Trim()
                    }
                }
            };

            var updated = await identityApi.UpdateOrganizationAsync(created.Id, updateRequest, ct);
            IdentityCommandHelpers.WriteJson(updated);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
