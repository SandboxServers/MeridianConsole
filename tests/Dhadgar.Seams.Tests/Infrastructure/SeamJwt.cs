using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// Test-only JWT support. The signing key is generated per process, so every host in one test run
/// validates the same tokens and no key material is committed.
/// </summary>
public static class SeamJwt
{
    public const string Issuer = "https://seams.test/identity";
    public const string Audience = "meridian-api";

    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Creates a user access token with <c>sub</c> and <c>org_id</c> claims, valid for 10 minutes.
    /// </summary>
    public static string CreateUserToken(Guid userId, Guid organizationId)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(SigningKey), SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim("sub", userId.ToString()),
                new Claim("org_id", organizationId.ToString())
            ],
            notBefore: now,
            expires: now.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Makes the JWT bearer scheme accept tokens from <see cref="CreateUserToken"/> without fetching metadata.
    /// Runs after the host's own configuration, so it replaces the validation parameters entirely.
    /// </summary>
    public static void Apply(IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.RequireHttpsMetadata = false;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration());
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(SigningKey),
                ClockSkew = TimeSpan.FromSeconds(5)
            };
        });
    }
}
