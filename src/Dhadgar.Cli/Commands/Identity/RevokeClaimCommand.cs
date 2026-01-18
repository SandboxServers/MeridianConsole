using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class RevokeClaimCommand
{
    public static async Task<int> ExecuteAsync(
        string memberId,
        string claimId,
        string? orgId,
        bool force,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(memberId))
        {
            return IdentityCommandHelpers.WriteError("member_id_required", "Member ID is required.");
        }

        if (string.IsNullOrWhiteSpace(claimId))
        {
            return IdentityCommandHelpers.WriteError("claim_id_required", "Claim ID is required.");
        }

        orgId ??= config.CurrentOrgId;
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return IdentityCommandHelpers.WriteError(
                "org_id_required",
                "Organization ID is required. Use --org or set a current org.");
        }

        // Confirm unless --force is specified
        if (!force)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Revoke claim '[cyan]{Markup.Escape(claimId)}[/]' from member?[/]",
                defaultValue: false);

            if (!confirm)
            {
                AnsiConsole.MarkupLine("[dim]Revocation cancelled.[/]");
                return 0;
            }
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        try
        {
            await identityApi.RemoveMemberClaimAsync(orgId, memberId, claimId, ct);

            AnsiConsole.MarkupLine($"[green]✓[/] Claim revoked successfully.");
            return 0;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            AnsiConsole.MarkupLine($"[red]Claim not found.[/] The claim may have already been revoked or expired.");
            return 1;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
