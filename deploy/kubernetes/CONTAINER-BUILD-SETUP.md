# Container Build Setup

This walkthrough sets up the container build pipeline and connects it to Azure Container Registry (ACR) and Kubernetes.

## Phase 0 - Prerequisites

- Azure subscription access with permission to read the ACR.
- Azure DevOps project with permissions to create service connections.
- Docker installed for local validation (optional but recommended).
- Helm v3 for Kubernetes deployments.

## Phase 1 - Verify ACR

Confirm the registry is available and reachable:

```
az acr show --name meridianconsoleacr --output table
az acr show-usage --name meridianconsoleacr --output table
```

If you cannot access the registry, request access to the `meridian-rg` resource group or ensure the proper subscription is selected.

## Phase 2 - Azure DevOps Service Connections

Create or verify these service connections in Azure DevOps:

1. **ACR service connection**
   - Name: `meridianconsoleacr`
   - Scope: `meridianconsoleacr` ACR resource

2. **GitHub service connection**
   - Name: `SandboxServers`
   - Used to pull the shared pipeline templates from `SandboxServers/Azure-Pipeline-YAML`

The container pipeline definition uses these by name. If your project uses different names, update `azure-pipelines-containers.yml` accordingly.

## Phase 3 - Pipeline Wiring

The file `azure-pipelines-containers.yml` in the repo root drives builds for all 13 services. It:

- Builds all Dockerfiles in parallel
- Pushes images to ACR
- Tags images with both build ID and `latest`
- Cleans up old images (keeps last 2 per service)

If you need to limit builds, adjust the `trigger.paths.include` list or the `services` section.

## Phase 4 - Local Validation (Optional)

```
# Build one image locally from the repo root
cd /mnt/c/Users/xxL0L/code_projects/MeridianConsole

docker build -f src/Dhadgar.Gateway/Dockerfile -t dhadgar/gateway:test .
```

If the build fails, confirm Docker is running and the .NET SDK image can be pulled.

## Phase 5 - Kubernetes Deployment

Ensure Helm values are configured to reference ACR:

```yaml
global:
  imageRegistry: "meridianconsoleacr-etdvg4cthscffqdf.azurecr.io"
```

Each service image repository uses the `dhadgar/*` namespace. Example:

```yaml
gateway:
  image:
    repository: dhadgar/gateway
    tag: latest
```

Then deploy:

```
cd deploy/kubernetes/helm/meridian-console
helm lint .
helm upgrade --install meridian-console . -n meridian --create-namespace
```

## Common Issues

- **Unauthorized / image pull errors**: verify the cluster can access ACR (managed identity or image pull secret).
- **Pipeline template errors**: check the GitHub service connection `SandboxServers` and that the template repo is reachable.
- **Tag not found**: make sure the pipeline ran and pushed `latest` for the service.

For detailed registry information, see `deploy/kubernetes/ACR-DETAILS.md`.
