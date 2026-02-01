using Dhadgar.Console;
using Dhadgar.Console.Hubs;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Add Dhadgar service defaults with Aspire-compatible patterns
builder.AddDhadgarServiceDefaults();

builder.Services.AddMeridianSwagger(
    title: "Dhadgar Console API",
    description: "Real-time console streaming via SignalR for Meridian Console");

builder.Services.AddSignalR();

var app = builder.Build();

app.UseMeridianSwagger();

// Dhadgar middleware pipeline (correlation, tenant enrichment, request logging)
app.UseDhadgarMiddleware();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Console", message = Dhadgar.Console.Hello.Message }))
    .WithTags("Health").WithName("ConsoleServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Console.Hello.Message))
    .WithTags("Health").WithName("ConsoleHello");
app.MapHub<ConsoleHub>("/hubs/console");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
