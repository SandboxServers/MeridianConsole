using Dhadgar.Scope;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Scope", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapGet("/healthz", () => Results.Ok(new { service = "Dhadgar.Scope", status = "ok" }));

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
