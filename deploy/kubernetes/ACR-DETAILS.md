# Azure Container Registry (ACR) Details

This project uses Azure Container Registry (ACR) as the canonical container image store for all microservices.

## Registry Information

- **ACR Name**: `meridianconsoleacr`
- **Login Server**: `meridianconsoleacr-etdvg4cthscffqdf.azurecr.io`
- **Resource Group**: `meridian-rg`
- **Location**: `centralus`
- **SKU**: Basic (10 GB storage limit)
- **Subscription**: `c87357b8-2149-476d-b91c-eb79095634ac`
- **Admin User**: Disabled (use service principals / managed identities)

## Image Naming Convention

All microservice images use the `dhadgar/*` namespace:

```
meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/dhadgar/gateway:latest
meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/dhadgar/identity:latest
```

## Authentication

```
# Log in with Azure CLI (requires `az login` first)
az acr login --name meridianconsoleacr

# Or use the full login server
az acr login --name meridianconsoleacr-etdvg4cthscffqdf.azurecr.io
```

If you need a service principal for CI/CD, prefer a least-privileged scope assignment on the ACR resource.

## Inspect ACR Contents

```
# List repositories
az acr repository list --name meridianconsoleacr --output table

# List tags for a repository
az acr repository show-tags \
  --name meridianconsoleacr \
  --repository dhadgar/gateway \
  --output table

# Show storage usage (Basic plan limit: 10 GB)
az acr show-usage --name meridianconsoleacr --output table
```

## Kubernetes Integration

If your cluster cannot access ACR via managed identity, create an image pull secret:

```
# Create a docker-registry secret (namespace example: meridian)
kubectl create secret docker-registry acr-pull-secret \
  --namespace meridian \
  --docker-server=meridianconsoleacr-etdvg4cthscffqdf.azurecr.io \
  --docker-username=<service-principal-id> \
  --docker-password=<service-principal-password>
```

Then wire it into Helm values:

```yaml
global:
  imagePullSecrets:
    - name: acr-pull-secret
```

## Notes

- The build pipeline `azure-pipelines-containers.yml` pushes images with dual tags: the build ID and `latest`.
- Cleanup keeps only the most recent 2 images per service to stay within the Basic SKU storage cap.
