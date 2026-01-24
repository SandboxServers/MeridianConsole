using Dhadgar.Discord;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.None);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Discord API",
    description: "Discord bot integration for Meridian Console");

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Discord", message = Dhadgar.Discord.Hello.Message }))
    .WithTags("Health").WithName("DiscordServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Discord.Hello.Message))
    .WithTags("Health").WithName("DiscordHello");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
