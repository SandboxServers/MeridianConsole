#!/bin/bash

# Meridian Console - Azure Container Registry Setup Script
# This script creates ACR and configures GitHub Actions authentication

set -e

echo "=========================================="
echo "🚀 Meridian Console ACR Setup"
echo "=========================================="
echo ""

# Configuration
ACR_NAME="meridianconsoleacr"  # Your ACR name from Azure
RESOURCE_GROUP="meridian-rg"
LOCATION="centralus"
SKU="Basic"  # Options: Basic, Standard, Premium
GITHUB_ORG="SandboxServers"
GITHUB_REPO="MeridianConsole"
APP_NAME="meridian-helm-publisher"

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo "❌ Azure CLI not found. Please install it first:"
    echo "   https://learn.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi

echo "🔍 Checking Azure login status..."
if ! az account show &> /dev/null; then
    echo "❌ Not logged in to Azure CLI"
    echo "   Please run: az login"
    exit 1
fi

# Get current Azure context
SUBSCRIPTION_ID=$(az account show --query id --output tsv 2>/dev/null)
SUBSCRIPTION_NAME=$(az account show --query name --output tsv 2>/dev/null)
TENANT_ID=$(az account show --query tenantId --output tsv 2>/dev/null)

if [ -z "$SUBSCRIPTION_ID" ]; then
    echo "❌ Unable to get Azure subscription information"
    echo "   Please ensure you're logged in: az login"
    exit 1
fi

echo "📋 Configuration:"
echo "  Subscription: $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)"
echo "  Tenant: $TENANT_ID"
echo "  Resource Group: $RESOURCE_GROUP"
echo "  ACR Name: $ACR_NAME"
echo "  Location: $LOCATION"
echo "  SKU: $SKU"
echo "  GitHub: $GITHUB_ORG/$GITHUB_REPO"
echo ""

echo "🔍 Checking existing resources..."
echo ""

# Check Resource Group
echo -n "  Resource Group ($RESOURCE_GROUP): "
if az group show --name $RESOURCE_GROUP --subscription $SUBSCRIPTION_ID &>/dev/null; then
    RG_LOCATION=$(az group show --name $RESOURCE_GROUP --query location --output tsv)
    echo "✅ EXISTS (location: $RG_LOCATION)"
    RG_EXISTS=true
else
    echo "❌ NOT FOUND (will create)"
    RG_EXISTS=false
fi

# Check ACR
echo -n "  ACR ($ACR_NAME): "
if az acr show --name $ACR_NAME --subscription $SUBSCRIPTION_ID &>/dev/null; then
    ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer --output tsv)
    ACR_SKU=$(az acr show --name $ACR_NAME --query sku.name --output tsv)
    ACR_RG=$(az acr show --name $ACR_NAME --query resourceGroup --output tsv)
    echo "✅ EXISTS (login: $ACR_LOGIN_SERVER, sku: $ACR_SKU, rg: $ACR_RG)"
    ACR_EXISTS=true
else
    echo "❌ NOT FOUND (will create)"
    ACR_EXISTS=false
fi

# Check Azure AD App
echo -n "  Azure AD App ($APP_NAME): "
EXISTING_APP=$(az ad app list --display-name $APP_NAME --query [0].appId --output tsv 2>/dev/null)
if [ -n "$EXISTING_APP" ]; then
    echo "✅ EXISTS (appId: $EXISTING_APP)"
    APP_EXISTS=true
else
    echo "❌ NOT FOUND (will create)"
    APP_EXISTS=false
fi

echo ""
read -p "Continue with setup? (y/N) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Cancelled."
    exit 0
fi

echo ""
echo "=========================================="
echo "📦 Step 1: Resource Group"
echo "=========================================="

if [ "$RG_EXISTS" = true ]; then
    echo "✅ Using existing resource group: $RESOURCE_GROUP"
else
    echo "Creating resource group: $RESOURCE_GROUP in $LOCATION"
    az group create \
        --name $RESOURCE_GROUP \
        --location $LOCATION \
        --subscription $SUBSCRIPTION_ID
    echo "✅ Resource group created"
fi

echo ""
echo "=========================================="
echo "📦 Step 2: Azure Container Registry"
echo "=========================================="

if [ "$ACR_EXISTS" = true ]; then
    echo "✅ Using existing ACR: $ACR_NAME"
    echo "   Login server: $ACR_LOGIN_SERVER"
    echo "   SKU: $ACR_SKU"
    echo "   Resource Group: $ACR_RG"

    # Verify it's in the right resource group
    if [ "$ACR_RG" != "$RESOURCE_GROUP" ]; then
        echo ""
        echo "⚠️  WARNING: ACR is in resource group '$ACR_RG' but script expects '$RESOURCE_GROUP'"
        read -p "Continue anyway? (y/N) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo "Cancelled. Please update RESOURCE_GROUP variable in script."
            exit 0
        fi
        # Use the actual resource group
        RESOURCE_GROUP=$ACR_RG
    fi
else
    echo "Creating ACR: $ACR_NAME"
    az acr create \
        --resource-group $RESOURCE_GROUP \
        --name $ACR_NAME \
        --sku $SKU \
        --location $LOCATION \
        --admin-enabled false \
        --subscription $SUBSCRIPTION_ID

    ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer --output tsv)
    echo "✅ ACR created: $ACR_LOGIN_SERVER"
fi

# Get ACR resource ID
ACR_ID=$(az acr show --name $ACR_NAME --subscription $SUBSCRIPTION_ID --query id --output tsv)
echo "   ACR Resource ID: $ACR_ID"

echo ""
echo "=========================================="
echo "🔐 Step 3: Azure AD Application"
echo "=========================================="

if [ "$APP_EXISTS" = true ]; then
    APP_ID=$EXISTING_APP
    echo "✅ Using existing app: $APP_NAME"
    echo "   App ID: $APP_ID"
else
    echo "Creating Azure AD application: $APP_NAME"
    APP_ID=$(az ad app create --display-name $APP_NAME --query appId --output tsv)
    echo "✅ App created: $APP_ID"
fi

# Create service principal if it doesn't exist
echo -n "Checking service principal: "
if az ad sp show --id $APP_ID &>/dev/null; then
    echo "✅ Already exists"
else
    echo "Creating..."
    az ad sp create --id $APP_ID
    echo "✅ Service principal created"
fi

echo ""
echo "=========================================="
echo "🔗 Step 4: Creating Federated Credentials"
echo "=========================================="

# Check if federated credential exists
EXISTING_CRED=$(az ad app federated-credential list --id $APP_ID --query "[?name=='github-main'].name" --output tsv)

if [ -n "$EXISTING_CRED" ]; then
    echo "⚠️  Federated credential 'github-main' already exists"
else
    echo "Creating federated credential for main branch..."
    az ad app federated-credential create \
        --id $APP_ID \
        --parameters "{
            \"name\": \"github-main\",
            \"issuer\": \"https://token.actions.githubusercontent.com\",
            \"subject\": \"repo:$GITHUB_ORG/$GITHUB_REPO:ref:refs/heads/main\",
            \"audiences\": [\"api://AzureADTokenExchange\"]
        }"
    echo "✅ Federated credential created for main branch"
fi

# Optional: Add credential for release tags
EXISTING_RELEASE_CRED=$(az ad app federated-credential list --id $APP_ID --query "[?name=='github-releases'].name" --output tsv)

if [ -n "$EXISTING_RELEASE_CRED" ]; then
    echo "✅ Federated credential 'github-releases' already exists"
else
    echo "Creating federated credential for release tags..."
    az ad app federated-credential create \
        --id $APP_ID \
        --parameters "{
            \"name\": \"github-releases\",
            \"issuer\": \"https://token.actions.githubusercontent.com\",
            \"subject\": \"repo:$GITHUB_ORG/$GITHUB_REPO:ref:refs/tags/*\",
            \"audiences\": [\"api://AzureADTokenExchange\"]
        }"
    echo "✅ Federated credential created for release tags"
fi

echo ""
echo "=========================================="
echo "🔑 Step 5: Granting ACR Permissions"
echo "=========================================="

# Check if role assignment exists
EXISTING_ROLE=$(az role assignment list \
    --assignee $APP_ID \
    --scope $ACR_ID \
    --query "[?roleDefinitionName=='AcrPush'].roleDefinitionName" \
    --output tsv)

if [ -n "$EXISTING_ROLE" ]; then
    echo "✅ AcrPush role already assigned"
else
    echo "Granting AcrPush role to service principal..."
    az role assignment create \
        --assignee $APP_ID \
        --role AcrPush \
        --scope $ACR_ID
    echo "✅ AcrPush role granted"
fi

echo ""
echo "=========================================="
echo "✅ Setup Complete!"
echo "=========================================="
echo ""
echo "📦 Azure Container Registry:"
echo "   Name: $ACR_NAME"
echo "   Login Server: $ACR_LOGIN_SERVER"
echo "   Resource Group: $RESOURCE_GROUP"
echo ""
echo "🔐 GitHub Secrets (add these to your repository):"
echo "   https://github.com/$GITHUB_ORG/$GITHUB_REPO/settings/secrets/actions"
echo ""
echo "   AZURE_CLIENT_ID: $APP_ID"
echo "   AZURE_TENANT_ID: $TENANT_ID"
echo "   AZURE_SUBSCRIPTION_ID: $SUBSCRIPTION_ID"
echo ""
echo "📝 GitHub Variables (optional, or edit workflow):"
echo "   https://github.com/$GITHUB_ORG/$GITHUB_REPO/settings/variables/actions"
echo ""
echo "   ACR_NAME: $ACR_NAME"
echo ""
echo "=========================================="
echo "🚀 Next Steps:"
echo "=========================================="
echo ""
echo "1. Add the secrets to GitHub (links above)"
echo "2. Add ACR_NAME variable OR edit .github/workflows/helm-publish.yml"
echo "3. Push to main or run workflow manually"
echo "4. Verify: az acr repository list --name $ACR_NAME"
echo ""
echo "📖 Documentation:"
echo "   - ACR-QUICKSTART.md"
echo "   - ACR-SETUP.md"
echo "   - PUBLISHING.md"
echo ""
