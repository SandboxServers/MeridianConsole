using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class CreateUserCommand
{
    public static async Task<int> ExecuteAsync(string email, string orgId, string? name, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return IdentityCommandHelpers.WriteError("email_required", "Email is required.");
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
        var request = new CreateUserRequest
        {
            Email = email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
        };

        try
        {
            var response = await identityApi.CreateUserAsync(orgId, request, ct);
            IdentityCommandHelpers.WriteJson(response);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
