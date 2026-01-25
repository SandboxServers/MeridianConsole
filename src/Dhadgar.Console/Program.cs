using Dhadgar.Console;
using Dhadgar.Console.Hubs;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.None);
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Console API",
    description: "Real-time console streaming via SignalR for Meridian Console");

builder.Services.AddSignalR();

var app = builder.Build();

app.UseMeridianSwagger();

// Standard middleware
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Console", message = Dhadgar.Console.Hello.Message }))
    .WithTags("Health").WithName("ConsoleServiceInfo");
app.MapGet("/hello", () => Results.Text(Dhadgar.Console.Hello.Message))
    .WithTags("Health").WithName("ConsoleHello");
app.MapHub<ConsoleHub>("/hubs/console");
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
