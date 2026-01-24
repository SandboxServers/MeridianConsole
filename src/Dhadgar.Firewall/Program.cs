using Dhadgar.Firewall;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.None);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Firewall API",
    description: "Port and firewall policy management for Meridian Console");

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Firewall", message = Dhadgar.Firewall.Hello.Message }))
    .WithTags("Health").WithName("FirewallServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Firewall.Hello.Message))
    .WithTags("Health").WithName("FirewallHello");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
