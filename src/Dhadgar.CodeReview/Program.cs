using Dhadgar.CodeReview;
using Dhadgar.CodeReview.Data;
using Dhadgar.CodeReview.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/codereview-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Configure Database (SQLite)
builder.Services.AddDbContext<CodeReviewDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("CodeReview")));

// Configure Options
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<ReviewOptions>(builder.Configuration.GetSection("Review"));

// Register Services
builder.Services.AddScoped<GitHubService>();
builder.Services.AddScoped<CouncilService>();
builder.Services.AddScoped<ReviewOrchestrator>();

// Register OllamaService with HttpClient
builder.Services.AddHttpClient<OllamaService>();

// Register ReviewQueueService as a hosted service
builder.Services.AddSingleton<ReviewQueueService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ReviewQueueService>());

var app = builder.Build();

// Apply EF Core migrations automatically on startup
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodeReviewDbContext>();
    db.Database.Migrate();
    Log.Information("Database migrations applied successfully");
}
catch (Exception ex)
{
    Log.Error(ex, "Database migration failed - service may not function correctly");
    throw; // Fail fast if database can't be initialized
}

// Configure middleware
app.MapOpenApi();
app.MapScalarApiReference();

app.UseSerilogRequestLogging();

app.MapControllers();

// Health check endpoints
app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.CodeReview",
    message = Hello.Message,
    version = "1.0.0"
}));

app.MapGet("/hello", () => Results.Text(Hello.Message));

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "Dhadgar.CodeReview",
    status = "ok",
    timestamp = DateTime.UtcNow
}));

// Diagnostic endpoint to check Ollama connection
app.MapGet("/diagnostics/ollama", async (OllamaService ollamaService) =>
{
    try
    {
        // Simple test - we'd need to implement a health check method in OllamaService
        return Results.Ok(new
        {
            status = "configured",
            message = "Ollama service is configured (call /review to test)"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ollama connection failed",
            detail: ex.Message,
            statusCode: 500);
    }
});

Log.Information("Starting Dhadgar.CodeReview service...");
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);

app.Run();

// Required for WebApplicationFactory<Program> integration tests
public partial class Program { }
