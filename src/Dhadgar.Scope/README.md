# Dhadgar.Scope (Blazor WebAssembly)

This project publishes the Meridian Console scoping document as a **static site**.

## Where the content lives
- `wwwroot/content/sections.json` — generated from `meridian-console-scope-v4.5.html` (source of truth for the scope doc)

## Local run
```bash
dotnet run --project src/Dhadgar.Scope
```

## Azure Static Web Apps
This publishes as static files, so it works well with Azure Static Web Apps (Free tier).
- `app_location`: `/src/Dhadgar.Scope`
- `output_location`: `wwwroot` (or the default publish output produced by SWA’s build)

If you use SWA’s default build, it will run `dotnet publish` and deploy the produced static output automatically.
