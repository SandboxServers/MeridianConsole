using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using Dhadgar.Cli.Utilities;
using Refit;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class GrantClaimCommand
{
    public static async Task<int> ExecuteAsync(
        string memberId,
        string permission,
        string? orgId,
        string? expiresIn,
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

        if (string.IsNullOrWhiteSpace(permission))
        {
            return IdentityCommandHelpers.WriteError("permission_required", "Permission is required.");
        }

        orgId ??= config.CurrentOrgId;
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return IdentityCommandHelpers.WriteError(
                "org_id_required",
                "Organization ID is required. Use --org or set a current org.");
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            return IdentityCommandHelpers.WriteError("invalid_config", error);
        }

        var identityApi = factory.CreateIdentityClient();

        DateTime? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(expiresIn))
        {
            expiresAt = ExpirationParser.Parse(expiresIn);
            if (expiresAt is null)
            {
                return IdentityCommandHelpers.WriteError(
                    "invalid_expiration",
                    "Invalid expiration format. Use: 1h, 1d, 7d, 30d, 1w, 1m, or ISO 8601 date.");
            }
        }

        var request = new AddClaimRequest
        {
            Type = "grant",
            Value = permission.Trim(),
            ExpiresAt = expiresAt
        };

        try
        {
            var response = await identityApi.AddMemberClaimAsync(orgId, memberId, request, ct);

            AnsiConsole.MarkupLine($"[green]✓[/] Permission granted successfully.");
            AnsiConsole.MarkupLine($"  [dim]Claim ID:[/] [cyan]{response.ClaimId}[/]");
            AnsiConsole.MarkupLine($"  [dim]Permission:[/] [cyan]{Markup.Escape(permission)}[/]");
            if (expiresAt.HasValue)
            {
                AnsiConsole.MarkupLine($"  [dim]Expires:[/] [yellow]{expiresAt.Value:u}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"  [dim]Expires:[/] [green]Never[/]");
            }
            return 0;
        }
        catch (ApiException ex)
        {
            return IdentityCommandHelpers.WriteApiError(ex);
        }
    }
}
