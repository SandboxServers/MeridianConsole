# ACR Helm Publishing - Quick Start

**TL;DR** - Get your Helm chart published to Azure Container Registry in 5 minutes.

## What You Need

- Azure Container Registry (e.g., `meridianacr.azurecr.io`)
- GitHub repository admin access
- Azure CLI

## Step 1: Setup ACR Authentication (One-Time)

Run this script to create a service principal and federated credentials:

```bash
#!/bin/bash
# Configure these variables
ACR_NAME="meridianacr"  # Your ACR name (without .azurecr.io)
GITHUB_ORG="SandboxServers"
GITHUB_REPO="MeridianConsole"

# Get Azure details
SUBSCRIPTION_ID=$(az account show --query id --output tsv)
TENANT_ID=$(az account show --query tenantId --output tsv)

# Create Azure AD app
APP_NAME="meridian-helm-publisher"
APP_ID=$(az ad app create --display-name $APP_NAME --query appId --output tsv)
az ad sp create --id $APP_ID

# Create federated credential for main branch
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:'$GITHUB_ORG'/'$GITHUB_REPO':ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Grant ACR push permission
ACR_ID=$(az acr show --name $ACR_NAME --query id --output tsv)
az role assignment create \
  --assignee $APP_ID \
  --role AcrPush \
  --scope $ACR_ID

# Output secrets for GitHub
echo ""
echo "✅ Setup complete! Add these to GitHub Secrets:"
echo ""
echo "AZURE_CLIENT_ID:       $APP_ID"
echo "AZURE_TENANT_ID:       $TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID: $SUBSCRIPTION_ID"
```

## Step 2: Configure GitHub

### Add Secrets

Go to: `https://github.com/SandboxServers/MeridianConsole/settings/secrets/actions`

Add these **Repository Secrets**:

| Name | Value (from script output) |
|------|---------------------------|
| `AZURE_CLIENT_ID` | `12345678-1234-...` |
| `AZURE_TENANT_ID` | `87654321-4321-...` |
| `AZURE_SUBSCRIPTION_ID` | `abcd1234-ab12-...` |

### Add Variable (Optional)

Go to: `https://github.com/SandboxServers/MeridianConsole/settings/variables/actions`

Add **Repository Variable**:

| Name | Value |
|------|-------|
| `ACR_NAME` | `meridianacr` |

Or edit `.github/workflows/helm-publish.yml` line 20:
```yaml
ACR_NAME: "meridianacr"  # Your ACR name
```

## Step 3: Publish!

### Option 1: Push to Main

```bash
# Make any change to the chart
cd deploy/kubernetes/helm/meridian-console
# Edit Chart.yaml version or any file

git add .
git commit -m "Update Helm chart"
git push origin main
```

Workflow triggers automatically!

### Option 2: Manual Trigger

1. Go to **Actions** → **Publish Helm Charts to ACR**
2. Click **Run workflow** → **Run workflow**

## Step 4: Verify

Check that it worked:

```bash
# List charts in ACR
az acr repository list --name meridianacr

# Should show: helm/meridian-console

# List versions
az acr repository show-tags \
  --name meridianacr \
  --repository helm/meridian-console
```

## Step 5: Install

Now anyone with ACR access can install:

```bash
# Login to ACR
az acr login --name meridianacr

# Install the chart
helm install meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace
```

## Common Issues

### "ACR not found"

Update `ACR_NAME` in the workflow or add it as a repository variable.

### "Login failed"

Double-check the three secrets are set correctly in GitHub.

### "Access denied"

Verify the service principal has `AcrPush` role:
```bash
az role assignment list --assignee $APP_ID
```

### "Workflow not triggering"

Workflow only triggers on changes to:
- `deploy/kubernetes/helm/meridian-console/**`
- `.github/workflows/helm-publish.yml`

Or use **Run workflow** button to trigger manually.

## What's Next?

- **Full setup docs**: [ACR-SETUP.md](ACR-SETUP.md)
- **Publishing guide**: [PUBLISHING.md](PUBLISHING.md)
- **Chart docs**: [meridian-console/README.md](meridian-console/README.md)

---

**That's it!** 🚀 Your Helm chart is now automatically published to ACR on every push.
