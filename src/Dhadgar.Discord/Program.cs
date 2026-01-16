using Dhadgar.Discord;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Discord API",
    description: "Discord bot integration for Meridian Console");

var app = builder.Build();

app.UseMeridianSwagger();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Discord", message = Hello.Message }))
    .WithTags("Health").WithName("DiscordServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("DiscordHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Discord", status = "ok" }))
    .WithTags("Health").WithName("DiscordHealth");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
