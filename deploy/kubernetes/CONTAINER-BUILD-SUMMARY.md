# Container Build Summary

This document summarizes the containerization architecture for Meridian Console and how images are produced, tagged, and deployed.

## Scope

- **13 microservices** are containerized using multi-stage Dockerfiles.
- **Azure DevOps pipeline** builds and pushes images to ACR.
- **Helm values** point at ACR and the `dhadgar/*` image namespace.

## Build Flow (High Level)

```
Developer Push
    |
    v
Azure DevOps Pipeline (azure-pipelines-containers.yml)
    |
    +--> Parallel Docker builds (13 services)
    |
    +--> Push to ACR (BuildId + latest tags)
    |
    +--> Cleanup (keep latest 2 per service)
    v
Kubernetes deploys from ACR via Helm values
```

## Dockerfile Strategy

Each service follows the same pattern:

- SDK build stage for `dotnet restore` and `dotnet publish`
- Runtime stage using `mcr.microsoft.com/dotnet/aspnet:10.0`
- Non-root `appuser` for security
- Health check against `/healthz`

This keeps image behavior consistent across all services.

## Image Naming

All images use the `dhadgar/*` namespace and are pushed to ACR:

```
meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/dhadgar/gateway:latest
meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/dhadgar/identity:<build-id>
```

## Storage Considerations

- ACR Basic has a 10 GB limit.
- Cleanup keeps only 2 images per service to reduce storage pressure.
- Actual storage varies per image size. Use:

```
az acr show-usage --name meridianconsoleacr --output table
```

If storage approaches the limit, consider:

- Increasing the SKU
- Reducing retained images
- Investigating unusually large image layers

## Troubleshooting Quick Reference

- **Build fails on restore**: ensure the Docker build context is repo root.
- **Pipeline cannot pull templates**: verify `SandboxServers` GitHub service connection.
- **Image pull failures in cluster**: confirm ACR auth (managed identity or image pull secret).
- **Tag not found**: ensure the pipeline ran on the branch and pushed `latest`.

## Related Docs

- `deploy/kubernetes/ACR-DETAILS.md`
- `deploy/kubernetes/CONTAINER-BUILD-SETUP.md`
- `deploy/kubernetes/CONTAINERIZATION_RECOVERY_PLAN.md`
