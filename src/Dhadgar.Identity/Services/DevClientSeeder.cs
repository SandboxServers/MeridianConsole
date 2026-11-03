using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Keeps the seeded dev client's organization binding in step with configuration.
/// </summary>
public static class DevClientSeeder
{
    /// <summary>
    /// Makes the stored binding of <paramref name="clientId"/> equal to <paramref name="organizationId"/>.
    /// Does nothing when the client does not exist or the binding already matches; otherwise updates the application.
    /// Logs the client id only, never the secret.
    /// </summary>
    public static async Task ReconcileOrganizationAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        Guid? organizationId,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(logger);

        var application = await manager.FindByClientIdAsync(clientId, ct);
        if (application is null)
        {
            return;
        }

        var properties = await manager.GetPropertiesAsync(application, ct);
        var current = ClientOrganizationBinding.Read(properties);
        var storedInvalid = current.IsFailure && current.Error == ClientOrganizationBinding.InvalidError;
        Guid? currentId = current.IsSuccess ? current.Value : null;

        if (!storedInvalid && currentId == organizationId)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application, ct);
        ClientOrganizationBinding.Apply(descriptor, organizationId);
        await manager.UpdateAsync(application, descriptor, ct);

        if (organizationId is null)
        {
            logger.LogInformation("Dev OpenIddict client {ClientId} unbound from its organization.", clientId);
        }
        else if (currentId is null && !storedInvalid)
        {
            logger.LogInformation("Dev OpenIddict client {ClientId} bound to an organization.", clientId);
        }
        else
        {
            logger.LogInformation("Dev OpenIddict client {ClientId} rebound to an organization.", clientId);
        }
    }
}
