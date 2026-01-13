using System.Net.Http.Json;
using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class UpdateOrgCommand
{
    public static async Task<int> ExecuteAsync(string orgId, string? name, string? description, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))
        {
            return IdentityCommandHelpers.WriteError(
                "missing_update_fields",
                "Provide --name and/or --description.");
        }

        using var client = IdentityCommandHelpers.CreateClient(config);
        var baseUrl = config.EffectiveIdentityUrl.TrimEnd('/');

        OrganizationSettingsResponse? settings = null;
        if (!string.IsNullOrWhiteSpace(description))
        {
            var getResponse = await client.GetAsync($"{baseUrl}/organizations/{orgId}", ct);
            var getBody = await getResponse.Content.ReadAsStringAsync(ct);

            if (!getResponse.IsSuccessStatusCode)
            {
                return IdentityCommandHelpers.WriteHttpError(getResponse, getBody);
            }

            var detail = IdentityCommandHelpers.Deserialize<OrganizationDetailResponse>(getBody);
            if (detail is null)
            {
                return IdentityCommandHelpers.WriteError("invalid_response", "Failed to parse organization detail.");
            }

            settings = detail.Settings ?? new OrganizationSettingsResponse();
            settings.CustomSettings ??= new Dictionary<string, string>();
            settings.CustomSettings["description"] = description.Trim();
        }

        var request = new UpdateOrganizationRequest
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Settings = settings
        };

        var response = await client.PatchAsJsonAsync($"{baseUrl}/organizations/{orgId}", request, ct);
        return await IdentityCommandHelpers.WriteJsonResponseAsync(response, ct);
    }
}
