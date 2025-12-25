# Dhadgar.Scope.Client

Blazor WebAssembly scope documentation site for Meridian Console.

## Run locally

```bash
dotnet run --project src/Dhadgar.Scope.Client
```

## Data content

Content is sourced from JSON files in `wwwroot/data/`.

## Deployment (Azure Static Web Apps)

The Static Web Apps configuration is in `wwwroot/staticwebapp.config.json`.

```bash
dotnet publish src/Dhadgar.Scope.Client -c Release -o _swa_publish
```
