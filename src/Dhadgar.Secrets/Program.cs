using Dhadgar.Secrets;
using Dhadgar.Secrets.Endpoints;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure options
builder.Services.Configure<SecretsOptions>(builder.Configuration.GetSection("Secrets"));

// Add memory cache for secret caching
builder.Services.AddMemoryCache();

// Authentication/authorization
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Auth:Issuer"];
        var metadataAddress = builder.Configuration["Auth:MetadataAddress"];

        options.Authority = issuer;
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        // Support separate MetadataAddress for internal service discovery in Docker/K8s
        // Token issuer remains external URL but JWKS is fetched from internal address
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer, // Explicitly set to match token issuer
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(
                builder.Configuration.GetValue<int?>("Auth:ClockSkewSeconds") ?? 60)
        };
        options.RefreshOnIssuerKeyNotFound = true;
    });

builder.Services.AddAuthorization();

var useDevelopmentProvider = builder.Configuration.GetValue<bool>("Secrets:UseDevelopmentProvider");

if (useDevelopmentProvider && builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ISecretProvider, DevelopmentSecretProvider>();
}
else
{
    // Default to Key Vault for non-dev environments or when explicitly configured.
    builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Standard service endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Secrets", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Secrets", status = "ok" }));

// Secrets API endpoints
app.MapSecretsEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
