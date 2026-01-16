using Dhadgar.Firewall;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Firewall API",
    description: "Port and firewall policy management for Meridian Console");

var app = builder.Build();

app.UseMeridianSwagger();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Firewall", message = Hello.Message }))
    .WithTags("Health").WithName("FirewallServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("FirewallHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Firewall", status = "ok" }))
    .WithTags("Health").WithName("FirewallHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
