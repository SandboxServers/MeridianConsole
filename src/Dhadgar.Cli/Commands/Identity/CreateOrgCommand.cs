using System.Text.Json;
using System.Net.Http.Json;
using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

public sealed class CreateOrgCommand
{
    public static async Task<int> ExecuteAsync(string name, string? description, CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode))
        {
            return exitCode;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return IdentityCommandHelpers.WriteError("name_required", "Organization name is required.");
        }

        using var client = IdentityCommandHelpers.CreateClient(config);
        var baseUrl = config.EffectiveIdentityUrl.TrimEnd('/');
        var request = new CreateOrganizationRequest { Name = name.Trim() };

        var response = await client.PostAsJsonAsync($"{baseUrl}/organizations", request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return IdentityCommandHelpers.WriteHttpError(response, body);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return IdentityCommandHelpers.WriteError("invalid_response", "Create response was empty.");
            }

            var element = JsonSerializer.Deserialize<JsonElement>(body);
            IdentityCommandHelpers.WriteJson(element);
            return 0;
        }

        var createResponse = IdentityCommandHelpers.Deserialize<JsonElement>(body);
        if (createResponse.ValueKind != JsonValueKind.Object ||
            !createResponse.TryGetProperty("id", out var idElement))
        {
            return IdentityCommandHelpers.WriteError("invalid_response", "Create response missing organization id.");
        }

        var orgId = idElement.GetString();
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return IdentityCommandHelpers.WriteError("invalid_response", "Create response contained an empty id.");
        }

        return await UpdateDescriptionAsync(client, baseUrl, orgId, description.Trim(), ct);
    }

    private static async Task<int> UpdateDescriptionAsync(
        HttpClient client,
        string baseUrl,
        string orgId,
        string description,
        CancellationToken ct)
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

        var settings = detail.Settings ?? new OrganizationSettingsResponse();
        settings.CustomSettings ??= new Dictionary<string, string>();
        settings.CustomSettings["description"] = description;

        var updateRequest = new UpdateOrganizationRequest
        {
            Settings = settings
        };

        var updateResponse = await client.PatchAsJsonAsync($"{baseUrl}/organizations/{orgId}", updateRequest, ct);
        return await IdentityCommandHelpers.WriteJsonResponseAsync(updateResponse, ct);
    }
}
