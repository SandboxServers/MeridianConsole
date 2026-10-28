# Static Web Apps publish output (Shopping Cart)

This project is a Blazor WebAssembly app designed to deploy to **Azure Static Web Apps** with an accompanying
**/api** Azure Functions folder.

## One command: build a SWA-ready folder

Running `dotnet publish` will still generate the normal .NET publish output under `bin/<Config>/<TFM>/publish`,
**and** it will additionally copy the deployable files into:

```
_swa_publish/wwwroot
_swa_publish/api
```

Those folders are ready for:

- `swa deploy _swa_publish/wwwroot --api-location _swa_publish/api --deployment-token <token>`
- Azure DevOps `AzureStaticWebApp@0` task with:
  - `output_location: _swa_publish/wwwroot`
  - `api_location: _swa_publish/api`
