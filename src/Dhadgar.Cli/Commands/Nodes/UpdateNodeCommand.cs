using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Dhadgar.Contracts.Nodes;
using Refit;

namespace Dhadgar.Cli.Commands.Nodes;

public sealed class UpdateNodeCommand
{
    public static async Task<int> ExecuteAsync(
        string nodeId,
        string? displayName,
        string? orgId,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!NodesCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        orgId ??= config.CurrentOrgId;
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return NodesCommandHelpers.WriteError(
                "org_id_required",
                "Organization ID is required. Use --org or set a current org.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return NodesCommandHelpers.WriteError(
                "no_updates",
                "At least one field to update must be provided (e.g., --display-name).");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return NodesCommandHelpers.WriteError("invalid_config", error);
        }

        var nodesApi = factory.CreateNodesClient();

        try
        {
            var request = new UpdateNodeRequest(Name: null, DisplayName: displayName);

            var response = await nodesApi.UpdateNodeAsync(orgId, nodeId, request, ct);
            NodesCommandHelpers.WriteJson(response);
            return 0;
        }
        catch (ApiException ex)
        {
            return NodesCommandHelpers.WriteApiError(ex);
        }
    }
}
