using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Nodes;

public sealed class DecommissionNodeCommand
{
    public static async Task<int> ExecuteAsync(
        string nodeId,
        string? orgId,
        bool force,
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

        if (!force)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Are you sure you want to decommission node '{nodeId}'?[/] This action cannot be undone.",
                defaultValue: false);

            if (!confirm)
            {
                AnsiConsole.MarkupLine("[dim]Operation cancelled.[/]");
                return 0;
            }
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return NodesCommandHelpers.WriteError("invalid_config", error);
        }

        var nodesApi = factory.CreateNodesClient();

        try
        {
            await nodesApi.DecommissionNodeAsync(orgId, nodeId, ct);
            NodesCommandHelpers.WriteJson(new { success = true, message = $"Node '{nodeId}' has been decommissioned." });
            return 0;
        }
        catch (ApiException ex)
        {
            return NodesCommandHelpers.WriteApiError(ex);
        }
    }
}
