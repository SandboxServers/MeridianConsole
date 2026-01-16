using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Dhadgar.Cli.Commands.Identity;
using Refit;

namespace Dhadgar.Cli.Commands.Me;

public sealed class RevokeSessionCommand
{
    public static async Task<int> ExecuteAsync(string sessionId, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return IdentityCommandHelpers.WriteError(
                "session_id_required",
                "Session ID is required.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        try
        {
            await identityApi.RevokeSessionAsync(sessionId.Trim(), ct);
            IdentityCommandHelpers.WriteJson(new { success = true, message = "Session revoked successfully" });
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
