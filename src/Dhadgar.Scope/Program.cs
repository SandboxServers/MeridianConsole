using System.Net.Http;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;

using Dhadgar.Scope;
using Dhadgar.Scope.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddScoped<ScopeContentService>();
builder.Services.AddScoped<DependencyGraphService>();
builder.Services.AddScoped<ArchitectureGraphService>();
builder.Services.AddScoped<DbSchemaCatalogService>();
builder.Services.AddScoped<CommMatrixService>();

// Configure MudBlazor with custom dark theme matching existing indigo/purple palette
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
});

await builder.Build().RunAsync();
