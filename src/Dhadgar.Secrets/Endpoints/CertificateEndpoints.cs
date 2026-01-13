using System.Collections.ObjectModel;
using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Dhadgar.Secrets.Endpoints;

public static class CertificateEndpoints
{
    public static void MapCertificateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Certificates")
            .RequireAuthorization();

        // List certificates
        group.MapGet("/certificates", ListCertificates)
            .WithName("ListCertificates")
            .WithDescription("List all certificates in the default Key Vault")
            .Produces<CertificatesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/keyvaults/{vaultName}/certificates", ListVaultCertificates)
            .WithName("ListVaultCertificates")
            .WithDescription("List certificates in a specific Key Vault")
            .Produces<CertificatesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        // Import certificate
        group.MapPost("/certificates", ImportCertificate)
            .WithName("ImportCertificate")
            .WithDescription("Import a certificate to the default Key Vault")
            .Produces<ImportCertificateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/keyvaults/{vaultName}/certificates", ImportVaultCertificate)
            .WithName("ImportVaultCertificate")
            .WithDescription("Import a certificate to a specific Key Vault")
            .Produces<ImportCertificateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        // Delete certificate
        group.MapDelete("/certificates/{name}", DeleteCertificate)
            .WithName("DeleteCertificate")
            .WithDescription("Delete a certificate from the default Key Vault")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListCertificates(
        [FromServices] ICertificateProvider provider,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasCertificatePermission(context.User, "read"))
        {
            return Results.Forbid();
        }

        var certificates = await provider.ListCertificatesAsync(null, ct);

        return Results.Ok(new CertificatesResponse(new Collection<CertificateItem>(certificates.Select(c => new CertificateItem(
            Name: c.Name,
            Subject: c.Subject,
            Issuer: c.Issuer,
            ExpiresAt: c.ExpiresAt,
            Thumbprint: c.Thumbprint,
            Enabled: c.Enabled
        )).ToList())));
    }

    private static async Task<IResult> ListVaultCertificates(
        string vaultName,
        [FromServices] ICertificateProvider provider,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasCertificatePermission(context.User, "read"))
        {
            return Results.Forbid();
        }

        var certificates = await provider.ListCertificatesAsync(vaultName, ct);

        return Results.Ok(new CertificatesResponse(new Collection<CertificateItem>(certificates.Select(c => new CertificateItem(
            Name: c.Name,
            Subject: c.Subject,
            Issuer: c.Issuer,
            ExpiresAt: c.ExpiresAt,
            Thumbprint: c.Thumbprint,
            Enabled: c.Enabled
        )).ToList())));
    }

    private static async Task<IResult> ImportCertificate(
        [FromBody] ImportCertificateRequest request,
        [FromServices] ICertificateProvider provider,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasCertificatePermission(context.User, "write"))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CertificateData))
        {
            return Results.BadRequest(new { error = "Name and CertificateData are required." });
        }

        try
        {
            var certBytes = Convert.FromBase64String(request.CertificateData);
            var result = await provider.ImportCertificateAsync(request.Name, certBytes, request.Password, null, ct);

            return Results.Ok(new ImportCertificateResponse(
                Name: result.Name,
                Subject: result.Subject,
                Issuer: result.Issuer,
                Thumbprint: result.Thumbprint,
                ExpiresAt: result.ExpiresAt
            ));
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "Invalid base64 encoded certificate data." });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new { error = ex.Message });
            }
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ImportVaultCertificate(
        string vaultName,
        [FromBody] ImportCertificateRequest request,
        [FromServices] ICertificateProvider provider,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasCertificatePermission(context.User, "write"))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CertificateData))
        {
            return Results.BadRequest(new { error = "Name and CertificateData are required." });
        }

        try
        {
            var certBytes = Convert.FromBase64String(request.CertificateData);
            var result = await provider.ImportCertificateAsync(request.Name, certBytes, request.Password, vaultName, ct);

            return Results.Ok(new ImportCertificateResponse(
                Name: result.Name,
                Subject: result.Subject,
                Issuer: result.Issuer,
                Thumbprint: result.Thumbprint,
                ExpiresAt: result.ExpiresAt
            ));
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "Invalid base64 encoded certificate data." });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new { error = ex.Message });
            }
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteCertificate(
        string name,
        [FromServices] ICertificateProvider provider,
        HttpContext context,
        CancellationToken ct)
    {
        if (!HasCertificatePermission(context.User, "write"))
        {
            return Results.Forbid();
        }

        var success = await provider.DeleteCertificateAsync(name, null, ct);

        if (!success)
        {
            return Results.NotFound(new { error = $"Certificate '{name}' not found." });
        }

        return Results.NoContent();
    }

    private static bool HasCertificatePermission(ClaimsPrincipal user, string action)
    {
        var permission = $"secrets:{action}:certificates";
        return user.Claims.Any(claim =>
            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
    }
}

public record CertificatesResponse(Collection<CertificateItem> Certificates);
public record CertificateItem(string Name, string Subject, string Issuer, DateTime ExpiresAt, string Thumbprint, bool Enabled);
public record ImportCertificateRequest(string Name, string CertificateData, string? Password);
public record ImportCertificateResponse(string Name, string Subject, string Issuer, string Thumbprint, DateTime ExpiresAt);
