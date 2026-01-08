using Dhadgar.Secrets;
using Dhadgar.Secrets.Endpoints;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure options
builder.Services.Configure<SecretsOptions>(builder.Configuration.GetSection("Secrets"));

// Add memory cache for secret caching
builder.Services.AddMemoryCache();

// Register the Key Vault secret provider
builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Standard service endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Secrets", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Secrets", status = "ok" }));

// Secrets API endpoints
app.MapSecretsEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
