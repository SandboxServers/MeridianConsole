<#
.SYNOPSIS
    Creates Azure App Registrations for Meridian Console platform.

.DESCRIPTION
    This script creates the required Azure AD App Registrations:
    1. Identity Service OIDC Issuer - Used for Workload Identity Federation (WIF)
    2. Secrets Service - Managed identity for Key Vault access
    3. Microsoft OAuth (Personal) - For user login with personal Microsoft accounts
    4. Microsoft OAuth (Work/School) - For user login with Azure AD accounts

    Key Vault Architecture:
    - mc-core: Identity service core secrets (certs, signing keys)
    - mc-oauth: Secrets service / OAuth secrets (OAuth credentials, infrastructure passwords)

.PARAMETER TenantId
    Azure AD Tenant ID

.PARAMETER SubscriptionId
    Azure subscription ID (default: c87357b8-2149-476d-b91c-eb79095634ac)

.PARAMETER Environment
    Environment name (dev, staging, prod) - affects app naming and redirect URIs

.PARAMETER CoreVaultName
    Name of the Core Key Vault for Identity service (default: mc-core)

.PARAMETER OAuthVaultName
    Name of the OAuth Key Vault for Secrets service (default: mc-oauth)

.EXAMPLE
    .\provision-azure-app-registrations.ps1 -TenantId "your-tenant-id" -Environment "dev"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [string]$SubscriptionId = "c87357b8-2149-476d-b91c-eb79095634ac",

    [ValidateSet("dev", "staging", "prod")]
    [string]$Environment = "dev",

    [string]$CoreVaultName = "mc-core",

    [string]$OAuthVaultName = "mc-oauth"
)

$ErrorActionPreference = "Stop"

# Environment-specific configuration
$envConfig = @{
    dev = @{
        BaseUrl = "https://localhost"
        IdentityUrl = "https://localhost:5001"
        PanelUrl = "https://localhost:4321"
        Suffix = "-dev"
    }
    staging = @{
        BaseUrl = "https://staging.meridianconsole.com"
        IdentityUrl = "https://staging.meridianconsole.com/api/v1/identity"
        PanelUrl = "https://staging.panel.meridianconsole.com"
        Suffix = "-staging"
    }
    prod = @{
        BaseUrl = "https://meridianconsole.com"
        IdentityUrl = "https://meridianconsole.com/api/v1/identity"
        PanelUrl = "https://panel.meridianconsole.com"
        Suffix = ""
    }
}

$config = $envConfig[$Environment]

Write-Host "=== Meridian Console Azure App Registrations ===" -ForegroundColor Cyan
Write-Host "Tenant: $TenantId" -ForegroundColor Gray
Write-Host "Environment: $Environment" -ForegroundColor Gray
Write-Host ""

# Ensure logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Please log in to Azure..." -ForegroundColor Yellow
    az login --tenant $TenantId
}

az account set --subscription $SubscriptionId

# ============================================================================
# 1. Identity Service OIDC Issuer (for Workload Identity Federation)
# ============================================================================
Write-Host ""
Write-Host "[1/4] Creating Identity Service OIDC Issuer App Registration..." -ForegroundColor Yellow

$identityAppName = "Meridian Console - Identity OIDC Issuer$($config.Suffix)"

$existingIdentityApp = az ad app list --display-name $identityAppName --query "[0]" 2>$null | ConvertFrom-Json

if ($existingIdentityApp) {
    Write-Host "  App '$identityAppName' already exists (ID: $($existingIdentityApp.appId))" -ForegroundColor Gray
    $identityAppId = $existingIdentityApp.appId
} else {
    # Create the app registration
    $identityApp = az ad app create `
        --display-name $identityAppName `
        --sign-in-audience "AzureADMyOrg" `
        --query "{appId: appId, id: id}" | ConvertFrom-Json

    $identityAppId = $identityApp.appId
    Write-Host "  Created '$identityAppName' (ID: $identityAppId)" -ForegroundColor Green

    # Create service principal
    az ad sp create --id $identityAppId | Out-Null
    Write-Host "  Created service principal" -ForegroundColor Green
}

# ============================================================================
# 2. Secrets Service App Registration
# ============================================================================
Write-Host ""
Write-Host "[2/4] Creating Secrets Service App Registration..." -ForegroundColor Yellow

$secretsAppName = "Meridian Console - Secrets Service$($config.Suffix)"

$existingSecretsApp = az ad app list --display-name $secretsAppName --query "[0]" 2>$null | ConvertFrom-Json

if ($existingSecretsApp) {
    Write-Host "  App '$secretsAppName' already exists (ID: $($existingSecretsApp.appId))" -ForegroundColor Gray
    $secretsAppId = $existingSecretsApp.appId
} else {
    $secretsApp = az ad app create `
        --display-name $secretsAppName `
        --sign-in-audience "AzureADMyOrg" `
        --query "{appId: appId, id: id}" | ConvertFrom-Json

    $secretsAppId = $secretsApp.appId
    Write-Host "  Created '$secretsAppName' (ID: $secretsAppId)" -ForegroundColor Green

    # Create service principal
    az ad sp create --id $secretsAppId | Out-Null
    Write-Host "  Created service principal" -ForegroundColor Green
}

# ============================================================================
# 3. Microsoft OAuth - Personal Accounts (consumers)
# ============================================================================
Write-Host ""
Write-Host "[3/4] Creating Microsoft OAuth App (Personal Accounts)..." -ForegroundColor Yellow

$msPersonalAppName = "Meridian Console - Login (Personal)$($config.Suffix)"

$existingMsPersonalApp = az ad app list --display-name $msPersonalAppName --query "[0]" 2>$null | ConvertFrom-Json

if ($existingMsPersonalApp) {
    Write-Host "  App '$msPersonalAppName' already exists (ID: $($existingMsPersonalApp.appId))" -ForegroundColor Gray
    $msPersonalAppId = $existingMsPersonalApp.appId
} else {
    # Personal accounts only - requires "PersonalMicrosoftAccount" audience
    # This uses the converged v2.0 endpoint
    $redirectUris = @(
        "$($config.IdentityUrl)/oauth/microsoft-personal/callback",
        "$($config.PanelUrl)/auth/callback/microsoft"
    )

    $msPersonalApp = az ad app create `
        --display-name $msPersonalAppName `
        --sign-in-audience "PersonalMicrosoftAccount" `
        --web-redirect-uris $redirectUris `
        --query "{appId: appId, id: id}" | ConvertFrom-Json

    $msPersonalAppId = $msPersonalApp.appId
    Write-Host "  Created '$msPersonalAppName' (ID: $msPersonalAppId)" -ForegroundColor Green

    # Create client secret
    $msPersonalSecret = az ad app credential reset `
        --id $msPersonalAppId `
        --display-name "Initial Secret" `
        --years 2 `
        --query "password" -o tsv

    Write-Host "  Created client secret (save this - it won't be shown again)" -ForegroundColor Yellow
    Write-Host "  Secret: $msPersonalSecret" -ForegroundColor Cyan

    # Store in OAuth Key Vault (for Secrets Service)
    az keyvault secret set `
        --vault-name $OAuthVaultName `
        --name "oauth-microsoft-personal-client-id" `
        --value $msPersonalAppId | Out-Null

    az keyvault secret set `
        --vault-name $OAuthVaultName `
        --name "oauth-microsoft-personal-client-secret" `
        --value $msPersonalSecret | Out-Null

    Write-Host "  Stored credentials in Key Vault ($OAuthVaultName)" -ForegroundColor Green
}

# ============================================================================
# 4. Microsoft OAuth - Work/School Accounts (Azure AD)
# ============================================================================
Write-Host ""
Write-Host "[4/4] Creating Microsoft OAuth App (Work/School Accounts)..." -ForegroundColor Yellow

$msWorkAppName = "Meridian Console - Login (Work/School)$($config.Suffix)"

$existingMsWorkApp = az ad app list --display-name $msWorkAppName --query "[0]" 2>$null | ConvertFrom-Json

if ($existingMsWorkApp) {
    Write-Host "  App '$msWorkAppName' already exists (ID: $($existingMsWorkApp.appId))" -ForegroundColor Gray
    $msWorkAppId = $existingMsWorkApp.appId
} else {
    # Work/school accounts - multi-tenant
    $redirectUris = @(
        "$($config.IdentityUrl)/oauth/microsoft-work/callback",
        "$($config.PanelUrl)/auth/callback/microsoft-work"
    )

    $msWorkApp = az ad app create `
        --display-name $msWorkAppName `
        --sign-in-audience "AzureADMultipleOrgs" `
        --web-redirect-uris $redirectUris `
        --query "{appId: appId, id: id}" | ConvertFrom-Json

    $msWorkAppId = $msWorkApp.appId
    Write-Host "  Created '$msWorkAppName' (ID: $msWorkAppId)" -ForegroundColor Green

    # Create client secret
    $msWorkSecret = az ad app credential reset `
        --id $msWorkAppId `
        --display-name "Initial Secret" `
        --years 2 `
        --query "password" -o tsv

    Write-Host "  Created client secret (save this - it won't be shown again)" -ForegroundColor Yellow
    Write-Host "  Secret: $msWorkSecret" -ForegroundColor Cyan

    # Store in OAuth Key Vault (for Secrets Service)
    az keyvault secret set `
        --vault-name $OAuthVaultName `
        --name "oauth-microsoft-work-client-id" `
        --value $msWorkAppId | Out-Null

    az keyvault secret set `
        --vault-name $OAuthVaultName `
        --name "oauth-microsoft-work-client-secret" `
        --value $msWorkSecret | Out-Null

    Write-Host "  Stored credentials in Key Vault ($OAuthVaultName)" -ForegroundColor Green
}

# ============================================================================
# Grant Key Vault Access to Identity Service (Core Vault)
# ============================================================================
Write-Host ""
Write-Host "Granting Key Vault access to Identity Service ($CoreVaultName)..." -ForegroundColor Yellow

$identitySp = az ad sp list --filter "appId eq '$identityAppId'" --query "[0].id" -o tsv

if ($identitySp) {
    # Grant Key Vault Secrets User role on Core vault
    az role assignment create `
        --assignee $identitySp `
        --role "Key Vault Secrets User" `
        --scope "/subscriptions/$SubscriptionId/resourceGroups/Secrets/providers/Microsoft.KeyVault/vaults/$CoreVaultName" `
        2>$null

    # Grant Key Vault Certificates User role on Core vault
    az role assignment create `
        --assignee $identitySp `
        --role "Key Vault Certificates User" `
        --scope "/subscriptions/$SubscriptionId/resourceGroups/Secrets/providers/Microsoft.KeyVault/vaults/$CoreVaultName" `
        2>$null

    Write-Host "  Granted Key Vault access to Identity Service ($CoreVaultName)" -ForegroundColor Green
}

# ============================================================================
# Grant Key Vault Access to Secrets Service (OAuth Vault)
# ============================================================================
Write-Host ""
Write-Host "Granting Key Vault access to Secrets Service ($OAuthVaultName)..." -ForegroundColor Yellow

$secretsSp = az ad sp list --filter "appId eq '$secretsAppId'" --query "[0].id" -o tsv

if ($secretsSp) {
    # Grant Key Vault Secrets User role (full read access to all secrets in OAuth vault)
    az role assignment create `
        --assignee $secretsSp `
        --role "Key Vault Secrets User" `
        --scope "/subscriptions/$SubscriptionId/resourceGroups/Secrets/providers/Microsoft.KeyVault/vaults/$OAuthVaultName" `
        2>$null

    Write-Host "  Granted Key Vault access to Secrets Service ($OAuthVaultName)" -ForegroundColor Green
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-Host "=== App Registration Summary ===" -ForegroundColor Green
Write-Host ""
Write-Host "Key Vault Architecture:" -ForegroundColor Magenta
Write-Host "  Core Vault ($CoreVaultName): Identity service core secrets (certs, signing keys)"
Write-Host "  OAuth Vault ($OAuthVaultName): Secrets service / OAuth secrets"
Write-Host ""
Write-Host "Identity OIDC Issuer:" -ForegroundColor Cyan
Write-Host "  App ID: $identityAppId"
Write-Host "  Key Vault: $CoreVaultName"
Write-Host "  Purpose: Workload Identity Federation for Identity service"
Write-Host ""
Write-Host "Secrets Service:" -ForegroundColor Cyan
Write-Host "  App ID: $secretsAppId"
Write-Host "  Key Vault: $OAuthVaultName"
Write-Host "  Purpose: Dispenses OAuth secrets to other services (BetterAuth, Identity OAuth providers)"
Write-Host ""
Write-Host "Microsoft OAuth (Personal):" -ForegroundColor Cyan
Write-Host "  App ID: $msPersonalAppId"
Write-Host "  Stored in: $OAuthVaultName"
Write-Host "  Purpose: User login with personal Microsoft accounts (@outlook.com, @hotmail.com, etc.)"
Write-Host ""
Write-Host "Microsoft OAuth (Work/School):" -ForegroundColor Cyan
Write-Host "  App ID: $msWorkAppId"
Write-Host "  Stored in: $OAuthVaultName"
Write-Host "  Purpose: User login with Azure AD accounts (enterprise users)"
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Configure Workload Identity Federation for Identity service in AKS"
Write-Host "2. Update Identity service configuration to use $CoreVaultName vault"
Write-Host "3. Update Secrets service configuration to use $OAuthVaultName vault"
Write-Host "4. Configure BetterAuth to call Secrets service for OAuth credentials"
Write-Host ""
