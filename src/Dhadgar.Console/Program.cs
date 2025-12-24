using Dhadgar.Console;
using Dhadgar.Console.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Console", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Console", status = "ok" }));
app.MapHub<ConsoleHub>("/hubs/console");

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
