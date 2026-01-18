using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class SwitchOrgCommand
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

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var response = await identityApi.SwitchOrganizationAsync(orgId, ct);

            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return IdentityCommandHelpers.WriteError("invalid_response", "Switch response missing access token.");
            }

            config.AccessToken = response.AccessToken;
            config.RefreshToken = response.RefreshToken;
            config.CurrentOrgId = response.OrganizationId ?? orgId;
            config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn - 60);
            config.Save();

            IdentityCommandHelpers.WriteJson(new
            {
                switched = true,
                organizationId = config.CurrentOrgId,
                expiresAt = config.TokenExpiresAt,
                permissions = response.Permissions?.ToArray() ?? Array.Empty<string>()
            });

            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
