using Dhadgar.Console;
using Dhadgar.Console.Hubs;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Console API",
    description: "Real-time console streaming via SignalR for Meridian Console");

builder.Services.AddSignalR();

var app = builder.Build();

app.UseMeridianSwagger();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Console", message = Hello.Message }))
    .WithTags("Health").WithName("ConsoleServiceInfo");
app.MapGet("/hello", () => Results.Text(Hello.Message))
    .WithTags("Health").WithName("ConsoleHello");
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Console", status = "ok" }))
    .WithTags("Health").WithName("ConsoleHealth");
app.MapHub<ConsoleHub>("/hubs/console");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
