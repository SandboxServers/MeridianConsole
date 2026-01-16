# Azure Container Registry Setup for Helm Charts

This guide explains how to configure GitHub Actions to automatically publish Helm charts to Azure Container Registry (ACR).

## Prerequisites

- Azure subscription with an Azure Container Registry
- GitHub repository with admin access
- Azure CLI installed (for local testing)

## Step 1: Create Azure Container Registry (if needed)

If you don't have an ACR yet:

```bash
# Set variables
RESOURCE_GROUP="meridian-rg"
ACR_NAME="meridianacr"  # Must be globally unique
LOCATION="eastus"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create ACR (Standard or Premium tier for production)
az acr create \
  --resource-group $RESOURCE_GROUP \
  --name $ACR_NAME \
  --sku Standard \
  --location $LOCATION

# Verify creation
az acr show --name $ACR_NAME --query loginServer --output tsv
# Output: meridianacr.azurecr.io
```

## Step 2: Create Service Principal or Managed Identity

Azure recommends using **Workload Identity Federation** (no secrets!) for GitHub Actions.

### Option A: Workload Identity Federation (Recommended)

This uses OpenID Connect (OIDC) - no secrets stored in GitHub!

```bash
# Set variables
ACR_NAME="meridianacr"
SUBSCRIPTION_ID=$(az account show --query id --output tsv)
RESOURCE_GROUP="meridian-rg"
GITHUB_ORG="SandboxServers"
GITHUB_REPO="MeridianConsole"

# Create an Azure AD application
APP_NAME="meridian-helm-publisher"
APP_ID=$(az ad app create --display-name $APP_NAME --query appId --output tsv)

# Create a service principal
az ad sp create --id $APP_ID

# Get service principal object ID
SP_OBJECT_ID=$(az ad sp list --filter "appId eq '$APP_ID'" --query [0].id --output tsv)

# Add federated credential for GitHub Actions (main branch)
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-main-branch",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:'$GITHUB_ORG'/'$GITHUB_REPO':ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Optional: Add credential for release tags
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-release-tags",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:'$GITHUB_ORG'/'$GITHUB_REPO':ref:refs/tags/*",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Get ACR resource ID
ACR_ID=$(az acr show --name $ACR_NAME --query id --output tsv)

# Grant AcrPush role to service principal (allows pushing images/charts)
az role assignment create \
  --assignee $APP_ID \
  --role AcrPush \
  --scope $ACR_ID

# Display values for GitHub secrets
echo "=== GitHub Repository Secrets ==="
echo "AZURE_CLIENT_ID: $APP_ID"
echo "AZURE_TENANT_ID: $(az account show --query tenantId --output tsv)"
echo "AZURE_SUBSCRIPTION_ID: $SUBSCRIPTION_ID"
```

### Option B: Service Principal with Secret (Legacy)

If you can't use Workload Identity Federation:

```bash
# Set variables
ACR_NAME="meridianacr"
SP_NAME="meridian-helm-publisher"

# Create service principal and get credentials
SP_CREDENTIALS=$(az ad sp create-for-rbac \
  --name $SP_NAME \
  --role AcrPush \
  --scopes $(az acr show --name $ACR_NAME --query id --output tsv) \
  --sdk-auth)

# Display credentials (save these securely!)
echo "$SP_CREDENTIALS"

# You'll get output like:
# {
#   "clientId": "...",
#   "clientSecret": "...",
#   "subscriptionId": "...",
#   "tenantId": "...",
#   ...
# }
```

## Step 3: Configure GitHub Repository Secrets

Go to your GitHub repository:
`https://github.com/SandboxServers/MeridianConsole/settings/secrets/actions`

### For Workload Identity Federation (Option A):

Add these **Repository Secrets**:

| Secret Name | Value | Example |
|-------------|-------|---------|
| `AZURE_CLIENT_ID` | Service Principal App ID | `12345678-1234-1234-1234-123456789012` |
| `AZURE_TENANT_ID` | Azure AD Tenant ID | `87654321-4321-4321-4321-210987654321` |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID | `abcd1234-ab12-cd34-ef56-1234567890ab` |

### For Service Principal with Secret (Option B):

Add these **Repository Secrets**:

| Secret Name | Value |
|-------------|-------|
| `AZURE_CREDENTIALS` | Entire JSON output from `az ad sp create-for-rbac` |

Then update the workflow to use:

```yaml
- name: Azure Login
  uses: azure/login@v2
  with:
    creds: ${{ secrets.AZURE_CREDENTIALS }}
```

## Step 4: Configure ACR Name

### Option 1: Repository Variable (Recommended)

Go to: `https://github.com/SandboxServers/MeridianConsole/settings/variables/actions`

Add a **Repository Variable**:

| Variable Name | Value |
|---------------|-------|
| `ACR_NAME` | Your ACR name (e.g., `meridianacr`) |

### Option 2: Edit Workflow File

Edit `.github/workflows/helm-publish.yml`:

```yaml
env:
  ACR_NAME: "meridianacr"  # Replace with your ACR name
```

## Step 5: Test the Workflow

### Trigger Manually

1. Go to **Actions** → **Publish Helm Charts to ACR**
2. Click **Run workflow**
3. Select `main` branch
4. Click **Run workflow**

### Or Push a Change

```bash
# Make a small change to trigger the workflow
cd deploy/kubernetes/helm/meridian-console
# Bump version in Chart.yaml
sed -i 's/version: 0.1.0/version: 0.1.1/' Chart.yaml

git add Chart.yaml
git commit -m "chore(helm): bump chart version to 0.1.1"
git push origin main
```

Watch the workflow run in the **Actions** tab.

## Step 6: Verify Chart in ACR

```bash
# List Helm charts in ACR
az acr repository list --name meridianacr --output table

# Show chart tags
az acr repository show-tags \
  --name meridianacr \
  --repository helm/meridian-console \
  --output table

# Show chart metadata
az acr repository show \
  --name meridianacr \
  --repository helm/meridian-console \
  --output json
```

## Installing from ACR

### Prerequisites for Users

Users need:
- Azure CLI installed
- Access to the ACR (via Azure RBAC)

### Installation Steps

```bash
# Login to Azure
az login

# Login to ACR (authenticates Docker/Helm)
az acr login --name meridianacr

# Install the chart
helm install meridian oci://meridianacr.azurecr.io/helm/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace
```

### For Kubernetes Clusters

If your Kubernetes cluster needs to pull from ACR:

#### Option 1: AKS with Managed Identity (Recommended)

```bash
# Attach ACR to AKS (automatic authentication)
az aks update \
  --resource-group meridian-rg \
  --name meridian-cluster \
  --attach-acr meridianacr
```

#### Option 2: Create Image Pull Secret

```bash
# Create service principal for ACR pull
ACR_PULL_SP=$(az ad sp create-for-rbac \
  --name meridian-acr-pull \
  --role AcrPull \
  --scopes $(az acr show --name meridianacr --query id --output tsv) \
  --query "{ clientId: appId, clientSecret: password }" \
  --output json)

CLIENT_ID=$(echo $ACR_PULL_SP | jq -r .clientId)
CLIENT_SECRET=$(echo $ACR_PULL_SP | jq -r .clientSecret)
ACR_LOGIN_SERVER="meridianacr.azurecr.io"

# Create Kubernetes secret
kubectl create secret docker-registry acr-credentials \
  --namespace meridian-system \
  --docker-server=$ACR_LOGIN_SERVER \
  --docker-username=$CLIENT_ID \
  --docker-password=$CLIENT_SECRET

# Reference in values.yaml
global:
  imagePullSecrets:
    - name: acr-credentials
```

## ACR Permissions

### Required Roles

| Role | Purpose | Scope |
|------|---------|-------|
| `AcrPush` | Push charts (GitHub Actions) | ACR |
| `AcrPull` | Pull charts (users, clusters) | ACR |
| `Reader` | List repositories | ACR or Resource Group |

### Grant User Access

```bash
# Grant user ability to pull charts
az role assignment create \
  --assignee user@example.com \
  --role AcrPull \
  --scope $(az acr show --name meridianacr --query id --output tsv)
```

## Troubleshooting

### Workflow Fails: "Login failed"

Check that secrets are set correctly:
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Verify federated credentials:
```bash
az ad app federated-credential list --id $APP_ID
```

### Workflow Fails: "ACR not found"

Check the `ACR_NAME` variable/environment in the workflow.

### Workflow Fails: "Access denied"

Verify service principal has `AcrPush` role:
```bash
az role assignment list \
  --assignee $APP_ID \
  --scope $(az acr show --name meridianacr --query id --output tsv)
```

### Can't Pull Chart: "Not authenticated"

```bash
# Re-authenticate
az login
az acr login --name meridianacr

# Verify login
az acr repository list --name meridianacr
```

### Helm Push Fails: "Insufficient scope"

The service principal needs `AcrPush` role, not just `AcrPull`.

## Security Best Practices

### 1. Use Workload Identity Federation

- ✅ No secrets stored in GitHub
- ✅ Automatic credential rotation
- ✅ Auditable via Azure AD logs

### 2. Least Privilege

Grant only required permissions:
- GitHub Actions: `AcrPush` on specific ACR
- Users: `AcrPull` on specific repositories
- Clusters: `AcrPull` via managed identity

### 3. Enable ACR Features

```bash
# Enable vulnerability scanning (Premium SKU)
az acr update --name meridianacr --anonymous-pull-enabled false

# Enable content trust (signed images)
az acr config content-trust update --status enabled --registry meridianacr

# Enable network restrictions
az acr network-rule add \
  --name meridianacr \
  --ip-address <your-ip>/32
```

### 4. Monitor Access

```bash
# View ACR activity logs
az monitor activity-log list \
  --resource-group meridian-rg \
  --resource-type Microsoft.ContainerRegistry/registries \
  --max-events 50
```

## Cost Optimization

- **Basic SKU**: $5/month - Good for dev/test
- **Standard SKU**: $20/month - Recommended for production
- **Premium SKU**: $50/month - Geo-replication, content trust

Storage costs: ~$0.10/GB/month

Cleanup old chart versions:
```bash
# Delete chart versions older than 90 days
az acr repository show-manifests \
  --name meridianacr \
  --repository helm/meridian-console \
  --orderby time_asc \
  --output tsv \
  | head -n -5 \
  | xargs -I {} az acr repository delete \
      --name meridianacr \
      --image helm/meridian-console@{} \
      --yes
```

## Summary

✅ **Setup completed when:**
1. ACR created
2. Service principal created with federated credentials
3. GitHub secrets configured
4. Workflow runs successfully
5. Chart appears in ACR

✅ **Users can install with:**
```bash
az acr login --name meridianacr
helm install meridian oci://meridianacr.azurecr.io/helm/meridian-console
```
