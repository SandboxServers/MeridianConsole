using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Dhadgar.Cli.Commands.Identity;
using Refit;

namespace Dhadgar.Cli.Commands.Me;

public sealed class ListLinkedAccountsCommand
{
    public static async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        try
        {
            var response = await identityApi.GetMyLinkedAccountsAsync(ct);
            IdentityCommandHelpers.WriteJson(response.LinkedAccounts);
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
