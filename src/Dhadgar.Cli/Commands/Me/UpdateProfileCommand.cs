using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Dhadgar.Cli.Commands.Identity;
using Refit;

namespace Dhadgar.Cli.Commands.Me;

public sealed class UpdateProfileCommand
{
    public static async Task<int> ExecuteAsync(
        string? displayName,
        string? preferredOrgId,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(preferredOrgId))
        {
            return IdentityCommandHelpers.WriteError(
                "no_updates",
                "At least one update (--display-name or --preferred-org) is required.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var request = new UpdateProfileRequest
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            PreferredOrganizationId = string.IsNullOrWhiteSpace(preferredOrgId) ? null : preferredOrgId.Trim()
        };

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var response = await identityApi.UpdateMyProfileAsync(request, ct);
            IdentityCommandHelpers.WriteJson(response);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
