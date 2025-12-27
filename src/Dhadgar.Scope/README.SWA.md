# Static Web Apps publish output (Scope)

This project is a Blazor WebAssembly app designed to deploy to **Azure Static Web Apps**.

## One command: build a SWA-ready folder

Running `dotnet publish` will still generate the normal .NET publish output under `bin/<Config>/<TFM>/publish`,
**and** it will additionally copy the deployable files into:

```
_swa_publish/wwwroot
```

That folder is ready for:

- `swa deploy _swa_publish/wwwroot --deployment-token <token>`
- Azure DevOps `AzureStaticWebApp@0` task with `output_location: _swa_publish/wwwroot`
