using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class DeleteUserCommand
{
    public static async Task<int> ExecuteAsync(string userId, string? orgId, bool force, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
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

        if (!force && !ConfirmDelete(userId))
        {
            IdentityCommandHelpers.WriteJson(new
            {
                deleted = false,
                userId,
                organizationId = orgId,
                cancelled = true
            });
            return 0;
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        try
        {
            await identityApi.DeleteUserAsync(orgId, userId, ct);

            IdentityCommandHelpers.WriteJson(new
            {
                deleted = true,
                userId,
                organizationId = orgId
            });
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }

    private static bool ConfirmDelete(string userId)
    {
        Console.Error.Write($"Delete user {userId}? [y/N]: ");
        var input = Console.ReadLine();
        return input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true ||
               input?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
    }
}
