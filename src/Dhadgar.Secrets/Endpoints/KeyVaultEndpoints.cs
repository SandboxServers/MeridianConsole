using System.Collections.ObjectModel;
using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Dhadgar.Secrets.Endpoints;

public static class KeyVaultEndpoints
{
    public static void MapKeyVaultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/keyvaults")
            .WithTags("Key Vaults")
            .RequireAuthorization();

        // List all Key Vaults
        group.MapGet("", ListVaults)
            .WithName("ListVaults")
            .WithDescription("List all Key Vaults in the subscription")
            .Produces<VaultsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        // Get vault details
        group.MapGet("/{vaultName}", GetVault)
            .WithName("GetVault")
            .WithDescription("Get detailed information about a specific Key Vault")
            .Produces<VaultDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // Create vault
        group.MapPost("", CreateVault)
            .WithName("CreateVault")
            .WithDescription("Create a new Key Vault")
            .Produces<VaultDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        // Update vault
        group.MapPatch("/{vaultName}", UpdateVault)
            .WithName("UpdateVault")
            .WithDescription("Update Key Vault properties")
            .Produces<VaultDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // Delete vault
        group.MapDelete("/{vaultName}", DeleteVault)
            .WithName("DeleteVault")
            .WithDescription("Delete a Key Vault (soft delete if enabled)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListVaults(
        [FromServices] IKeyVaultManager manager,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasVaultPermission(context.User, "read"))
        {
            return Results.Forbid();
        }

        var vaults = await manager.ListVaultsAsync(ct);

        return Results.Ok(new VaultsResponse(new Collection<VaultItem>(vaults.Select(v => new VaultItem(
            Name: v.Name,
            VaultUri: v.VaultUri,
            Location: v.Location,
            SecretCount: v.SecretCount,
            Enabled: v.Enabled
        )).ToList())));
    }

    private static async Task<IResult> GetVault(
        string vaultName,
        [FromServices] IKeyVaultManager manager,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasVaultPermission(context.User, "read"))
        {
            return Results.Forbid();
        }

        var vault = await manager.GetVaultAsync(vaultName, ct);

        if (vault == null)
        {
            return Results.Problem(
                detail: $"Vault '{vaultName}' not found.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                type: "https://meridian.console/errors/not-found");
        }

        return Results.Ok(new VaultDetailResponse(
            Name: vault.Name,
            VaultUri: vault.VaultUri,
            Location: vault.Location,
            ResourceGroup: vault.ResourceGroup,
            Sku: vault.Sku,
            TenantId: vault.TenantId,
            EnableSoftDelete: vault.EnableSoftDelete,
            EnablePurgeProtection: vault.EnablePurgeProtection,
            SoftDeleteRetentionDays: vault.SoftDeleteRetentionDays,
            EnableRbacAuthorization: vault.EnableRbacAuthorization,
            PublicNetworkAccess: vault.PublicNetworkAccess,
            SecretCount: vault.SecretCount,
            KeyCount: vault.KeyCount,
            CertificateCount: vault.CertificateCount,
            CreatedAt: vault.CreatedAt,
            UpdatedAt: vault.UpdatedAt
        ));
    }

    private static async Task<IResult> CreateVault(
        [FromBody] CreateVaultRequest request,
        [FromServices] IKeyVaultManager manager,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasVaultPermission(context.User, "write"))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Location))
        {
            return Results.Problem(
                detail: "Name and Location are required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://meridian.console/errors/validation");
        }

        try
        {
            var createRequest = new Services.CreateVaultRequest(request.Name, request.Location, request.ResourceGroupName);
            var vault = await manager.CreateVaultAsync(createRequest, ct);

            return Results.Ok(new VaultDetailResponse(
                Name: vault.Name,
                VaultUri: vault.VaultUri,
                Location: vault.Location,
                ResourceGroup: vault.ResourceGroup,
                Sku: vault.Sku,
                TenantId: vault.TenantId,
                EnableSoftDelete: vault.EnableSoftDelete,
                EnablePurgeProtection: vault.EnablePurgeProtection,
                SoftDeleteRetentionDays: vault.SoftDeleteRetentionDays,
                EnableRbacAuthorization: vault.EnableRbacAuthorization,
                PublicNetworkAccess: vault.PublicNetworkAccess,
                SecretCount: vault.SecretCount,
                KeyCount: vault.KeyCount,
                CertificateCount: vault.CertificateCount,
                CreatedAt: vault.CreatedAt,
                UpdatedAt: vault.UpdatedAt
            ));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://meridian.console/errors/bad-request");
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("exists", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    type: "https://meridian.console/errors/conflict");
            }
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://meridian.console/errors/bad-request");
        }
    }

    private static async Task<IResult> UpdateVault(
        string vaultName,
        [FromBody] UpdateVaultRequest request,
        [FromServices] IKeyVaultManager manager,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasVaultPermission(context.User, "write"))
        {
            return Results.Forbid();
        }

        try
        {
            var updateRequest = new Services.UpdateVaultRequest(
                EnableSoftDelete: request.EnableSoftDelete,
                EnablePurgeProtection: request.EnablePurgeProtection,
                SoftDeleteRetentionDays: request.SoftDeleteRetentionDays,
                Sku: request.Sku
            );

            var vault = await manager.UpdateVaultAsync(vaultName, updateRequest, ct);

            return Results.Ok(new VaultDetailResponse(
                Name: vault.Name,
                VaultUri: vault.VaultUri,
                Location: vault.Location,
                ResourceGroup: vault.ResourceGroup,
                Sku: vault.Sku,
                TenantId: vault.TenantId,
                EnableSoftDelete: vault.EnableSoftDelete,
                EnablePurgeProtection: vault.EnablePurgeProtection,
                SoftDeleteRetentionDays: vault.SoftDeleteRetentionDays,
                EnableRbacAuthorization: vault.EnableRbacAuthorization,
                PublicNetworkAccess: vault.PublicNetworkAccess,
                SecretCount: vault.SecretCount,
                KeyCount: vault.KeyCount,
                CertificateCount: vault.CertificateCount,
                CreatedAt: vault.CreatedAt,
                UpdatedAt: vault.UpdatedAt
            ));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://meridian.console/errors/bad-request");
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    type: "https://meridian.console/errors/not-found");
            }
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://meridian.console/errors/bad-request");
        }
    }

    private static async Task<IResult> DeleteVault(
        string vaultName,
        [FromServices] IKeyVaultManager manager,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasVaultPermission(context.User, "write"))
        {
            return Results.Forbid();
        }

        var success = await manager.DeleteVaultAsync(vaultName, false, ct);

        if (!success)
        {
            return Results.Problem(
                detail: $"Vault '{vaultName}' not found.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                type: "https://meridian.console/errors/not-found");
        }

        return Results.NoContent();
    }

    private static bool HasVaultPermission(ClaimsPrincipal user, string action)
    {
        var permission = $"keyvault:{action}";
        return user.Claims.Any(claim =>
            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
    }
}

public record VaultsResponse(Collection<VaultItem> Vaults);
public record VaultItem(string Name, Uri VaultUri, string Location, int SecretCount, bool Enabled);

public record VaultDetailResponse(
    string Name,
    Uri VaultUri,
    string Location,
    string ResourceGroup,
    string Sku,
    string TenantId,
    bool EnableSoftDelete,
    bool EnablePurgeProtection,
    int SoftDeleteRetentionDays,
    bool EnableRbacAuthorization,
    string PublicNetworkAccess,
    int SecretCount,
    int KeyCount,
    int CertificateCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateVaultRequest(string Name, string Location, string? ResourceGroupName = null);
public record UpdateVaultRequest(bool? EnableSoftDelete = null, bool? EnablePurgeProtection = null, int? SoftDeleteRetentionDays = null, string? Sku = null);
