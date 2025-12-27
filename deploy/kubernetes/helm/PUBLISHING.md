# Publishing Helm Charts to Azure Container Registry

This guide explains how to publish the Meridian Console Helm chart to Azure Container Registry (ACR) so users can install it as an OCI artifact.

## Overview

The chart is published to **Azure Container Registry** using a **GitHub Actions workflow** that:
1. Packages the chart automatically on changes
2. Authenticates with ACR using Workload Identity Federation
3. Pushes the chart as an OCI artifact
4. Makes it available at: `oci://your-acr.azurecr.io/helm/meridian-console`

## Initial Setup (One-Time)

**See [ACR-SETUP.md](ACR-SETUP.md) for detailed setup instructions.**

### Quick Setup Summary

1. **Create Azure Container Registry** (if needed)
   ```bash
   az acr create --resource-group meridian-rg --name meridianacr --sku Standard
   ```

2. **Create Service Principal with Federated Identity**
   ```bash
   # See ACR-SETUP.md for complete script
   az ad app create --display-name meridian-helm-publisher
   az ad app federated-credential create --id $APP_ID ...
   az role assignment create --role AcrPush --scope $ACR_ID
   ```

3. **Configure GitHub Secrets**
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`

4. **Configure ACR Name**
   - Add `ACR_NAME` repository variable in GitHub
   - Or edit `.github/workflows/helm-publish.yml`

The workflow file is located at:
```
.github/workflows/helm-publish.yml
```

It will automatically trigger on:
- Push to `main` branch (if chart files changed)
- Published releases
- Manual workflow dispatch

## How It Works

### Automatic Publishing

When you push changes to the Helm chart in `deploy/kubernetes/helm/meridian-console/`:

1. **GitHub Actions triggers** the `helm-publish.yml` workflow
2. **Authenticates with Azure** using Workload Identity Federation
3. **Logs in to ACR** using Azure CLI
4. **Updates dependencies** (downloads PostgreSQL, RabbitMQ, Redis charts)
5. **Lints and packages** the chart
6. **Pushes to ACR** as an OCI artifact at `oci://your-acr.azurecr.io/helm/meridian-console`

### Manual Publishing

You can also trigger the workflow manually:

1. Go to **Actions** → **Publish Helm Charts**
2. Click **Run workflow**
3. Select branch (usually `main`)
4. Click **Run workflow**

## Using the Published Chart

### Prerequisites

Users need:
- Helm 3.8+ (for OCI support)
- Azure CLI
- Access to the ACR (via Azure RBAC)

### Authenticate with ACR

```bash
# Login to Azure
az login

# Login to ACR (authenticates Helm)
az acr login --name meridianacr
```

### Install the Chart

```bash
# Install with defaults (no helm repo add needed!)
helm install meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace

# Install with custom values
helm install meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace \
  --values custom-values.yaml

# Install latest version (omit --version)
helm install meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --namespace meridian-system \
  --create-namespace
```

### List Available Versions

```bash
# Using Azure CLI
az acr repository show-tags \
  --name meridianacr \
  --repository helm/meridian-console \
  --output table
```

### Upgrade Existing Installation

```bash
# Re-authenticate if needed
az acr login --name meridianacr

# Upgrade to specific version
helm upgrade meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --version 0.2.0 \
  --namespace meridian-system

# Upgrade to latest
helm upgrade meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --namespace meridian-system
```

## Versioning Strategy

### Chart Version

The chart version is defined in `Chart.yaml`:

```yaml
version: 0.1.0
```

**Semantic Versioning (SemVer):**
- **MAJOR** (1.0.0) - Breaking changes
- **MINOR** (0.1.0) - New features, backwards compatible
- **PATCH** (0.0.1) - Bug fixes, backwards compatible

### Releasing New Versions

#### Option 1: Update Chart.yaml and Push

1. Edit `deploy/kubernetes/helm/meridian-console/Chart.yaml`
   ```yaml
   version: 0.2.0  # Increment version
   appVersion: "0.2.0"  # Update app version if needed
   ```

2. Commit and push:
   ```bash
   git add deploy/kubernetes/helm/meridian-console/Chart.yaml
   git commit -m "chore(helm): bump chart version to 0.2.0"
   git push origin main
   ```

3. GitHub Actions automatically publishes the new version

#### Option 2: GitHub Release

1. Create a git tag:
   ```bash
   git tag -a v0.2.0 -m "Release version 0.2.0"
   git push origin v0.2.0
   ```

2. Create a GitHub release:
   - Go to **Releases** → **Create a new release**
   - Choose the tag `v0.2.0`
   - Write release notes
   - Click **Publish release**

3. GitHub Actions automatically publishes on release

## Advanced: OCI Registry (Alternative)

GitHub also supports Helm charts as OCI artifacts in **GitHub Container Registry (GHCR)**.

### Publish to GHCR

```bash
# Login to GHCR
echo $GITHUB_TOKEN | helm registry login ghcr.io -u USERNAME --password-stdin

# Package chart
helm package deploy/kubernetes/helm/meridian-console

# Push to GHCR
helm push meridian-console-0.1.0.tgz oci://ghcr.io/sandboxservers
```

### Install from GHCR

```bash
# Install directly from OCI registry (no helm repo add needed)
helm install meridian oci://ghcr.io/sandboxservers/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace
```

**Pros:**
- No GitHub Pages setup required
- OCI standard, works with any OCI registry
- Better for private charts (GHCR supports authentication)

**Cons:**
- Requires Helm 3.8+
- Different installation syntax
- Less discoverable (no `helm search`)

## Troubleshooting

### Chart Not Appearing After Push

1. **Check GitHub Actions**: Go to **Actions** tab, ensure workflow succeeded
2. **Check gh-pages branch**: Verify the `.tgz` file and `index.yaml` exist
3. **Check GitHub Pages**: Ensure Pages is enabled and serving from `gh-pages`
4. **Clear Helm cache**: `helm repo update meridian`

### Repository Not Found

```bash
# Verify the URL is correct
curl https://sandboxservers.github.io/MeridianConsole/index.yaml

# Should return the Helm repository index
```

### Old Version Installing

```bash
# Update the repository
helm repo update meridian

# List available versions
helm search repo meridian/meridian-console --versions

# Install specific version
helm install meridian meridian/meridian-console --version 0.2.0
```

### Workflow Failing

1. Check workflow logs in **Actions** tab
2. Common issues:
   - Missing dependencies (Bitnami charts)
   - Lint errors in templates
   - Permission issues (ensure `permissions:` is set in workflow)

### Dependencies Not Updating

The workflow runs `helm dependency update` automatically. If you need to update locally:

```bash
cd deploy/kubernetes/helm/meridian-console
helm dependency update
```

## Repository Maintenance

### Cleanup Old Versions

Over time, the `gh-pages` branch will accumulate old chart versions. To clean up:

```bash
# Checkout gh-pages
git checkout gh-pages

# Keep only the last 5 versions of each chart
ls -t meridian-console-*.tgz | tail -n +6 | xargs rm -f

# Rebuild index
helm repo index . --url https://sandboxservers.github.io/MeridianConsole/

# Commit and push
git add .
git commit -m "Clean up old chart versions"
git push origin gh-pages

git checkout main
```

### Multiple Charts

If you add more charts in the future:

```
deploy/kubernetes/helm/
├── meridian-console/
├── another-chart/
└── yet-another-chart/
```

The workflow will need updating to package all charts:

```yaml
- name: Package Helm charts
  run: |
    cd deploy/kubernetes/helm
    mkdir -p packages
    for chart in */; do
      helm package "$chart" --destination ./packages
    done
```

## Security Considerations

### Public vs Private Charts

- **GitHub Pages** = Public (anyone can install)
- **GHCR (OCI)** = Can be private (requires authentication)

For private charts:
1. Use GHCR with OCI
2. Generate Personal Access Token (PAT) with `read:packages` scope
3. Users login: `helm registry login ghcr.io -u USERNAME`

### Signed Charts

For production, consider signing charts with GPG:

```bash
# Sign chart
helm package --sign --key 'John Doe' --keyring ~/.gnupg/secring.gpg meridian-console

# Verify signed chart
helm verify meridian-console-0.1.0.tgz --keyring ~/.gnupg/pubring.gpg
```

## Summary

✅ **One-time setup:**
1. Enable GitHub Pages (gh-pages branch)
2. Create gh-pages branch with initial index
3. GitHub Actions workflow is already configured

✅ **Publishing new versions:**
1. Update `Chart.yaml` version
2. Push to main or create a GitHub release
3. Workflow automatically publishes

✅ **Users install with:**
```bash
helm repo add meridian https://sandboxservers.github.io/MeridianConsole/
helm install meridian meridian/meridian-console
```

That's it! 🚀
