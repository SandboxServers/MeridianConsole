using Dhadgar.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Gateway", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Gateway", status = "ok" }));
app.MapReverseProxy();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
