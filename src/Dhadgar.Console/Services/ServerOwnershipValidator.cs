using System.Net;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Console.Services;

public sealed class ServerOwnershipValidator : IServerOwnershipValidator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerOwnershipValidator> _logger;

    public ServerOwnershipValidator(
        HttpClient httpClient,
        ILogger<ServerOwnershipValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> ValidateOwnershipAsync(Guid serverId, Guid organizationId, CancellationToken ct = default)
    {
        try
        {
            // Call Servers API to verify the server belongs to the organization
            var response = await _httpClient.GetAsync(
                $"/organizations/{organizationId}/servers/{serverId}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Server {ServerId} not found in org {OrgId}", serverId, organizationId);
                return false;
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate server {ServerId} ownership for org {OrgId}",
                serverId, organizationId);
            return false;
        }
    }
}
