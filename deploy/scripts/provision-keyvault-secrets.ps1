<#
.SYNOPSIS
    Provisions all required secrets for Meridian Console in Azure Key Vault.

.DESCRIPTION
    This script creates all certificates, keys, and secrets required by the platform.
    Secrets are split across two vaults:
    - Core vault (mc-core): Identity service secrets (certs, signing keys, exchange keys)
    - OAuth vault (mc-oauth): OAuth provider secrets and infrastructure passwords

    By default, existing secrets are skipped. Use -Update to compare and update if different.

.PARAMETER CoreVaultName
    The Azure Key Vault name for core identity secrets (default: mc-core)

.PARAMETER OAuthVaultName
    The Azure Key Vault name for OAuth and infrastructure secrets (default: mc-oauth)

.PARAMETER SubscriptionId
    Azure subscription ID

.PARAMETER Update
    If specified, compares existing secrets and updates them if the value has changed.

.PARAMETER CreateVaults
    If specified, creates the Key Vaults if they don't exist.

.PARAMETER GrantAccess
    If specified, grants the current user Key Vault Administrator role on both vaults.
    Use this if you get "Caller is not authorized" errors.

.EXAMPLE
    .\provision-keyvault-secrets.ps1

.EXAMPLE
    .\provision-keyvault-secrets.ps1 -Update

.EXAMPLE
    .\provision-keyvault-secrets.ps1 -CreateVaults -Update

.EXAMPLE
    .\provision-keyvault-secrets.ps1 -GrantAccess
#>

param(
    [string]$CoreVaultName = "mc-core",
    [string]$OAuthVaultName = "mc-oauth",
    [string]$SubscriptionId = "c87357b8-2149-476d-b91c-eb79095634ac",
    [string]$ResourceGroup = "Secrets",
    [string]$Location = "eastus",
    [switch]$Update,
    [switch]$CreateVaults,
    [switch]$GrantAccess
)

$ErrorActionPreference = "Stop"

Write-Host "=== Meridian Console Key Vault Secrets Provisioning ===" -ForegroundColor Cyan
Write-Host "Core Vault:  $CoreVaultName (Identity service)" -ForegroundColor Gray
Write-Host "OAuth Vault: $OAuthVaultName (Secrets service)" -ForegroundColor Gray
if ($Update) {
    Write-Host "Mode: UPDATE (will compare and update existing secrets if different)" -ForegroundColor Yellow
}
Write-Host ""

# Ensure logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Please log in to Azure..." -ForegroundColor Yellow
    az login
}

# Set subscription
az account set --subscription $SubscriptionId
Write-Host "Using subscription: $SubscriptionId" -ForegroundColor Gray
Write-Host ""

# Helper function to set a secret, comparing if it already exists
function Set-SecretIfNeeded {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$Value,
        [string]$ContentType = $null
    )

    $existing = az keyvault secret show --vault-name $VaultName --name $SecretName --query value -o tsv 2>$null

    if ($existing) {
        if (-not $Update) {
            Write-Host "  Secret '$SecretName' already exists, skipping. Use -Update to compare." -ForegroundColor Gray
            return $false
        }

        if ($existing -eq $Value) {
            Write-Host "  Secret '$SecretName' unchanged, skipping." -ForegroundColor Gray
            return $false
        }

        Write-Host "  Updating '$SecretName' (value changed)..." -ForegroundColor Yellow
    }

    $azArgs = @("keyvault", "secret", "set", "--vault-name", $VaultName, "--name", $SecretName, "--value", $Value)
    if ($ContentType) {
        $azArgs += "--content-type"
        $azArgs += $ContentType
    }
    az @azArgs | Out-Null

    if ($existing) {
        Write-Host "  Updated '$SecretName'" -ForegroundColor Green
    } else {
        Write-Host "  Created '$SecretName'" -ForegroundColor Green
    }
    return $true
}

# Helper function to set a secret from file, comparing if it already exists
function Set-SecretFromFileIfNeeded {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$FilePath,
        [string]$ContentType = $null
    )

    $newValue = Get-Content $FilePath -Raw
    $existing = az keyvault secret show --vault-name $VaultName --name $SecretName --query value -o tsv 2>$null

    if ($existing) {
        if (-not $Update) {
            Write-Host "  Secret '$SecretName' already exists, skipping. Use -Update to compare." -ForegroundColor Gray
            return $false
        }

        # Normalize line endings for comparison
        $existingNorm = $existing -replace "`r`n", "`n"
        $newValueNorm = $newValue -replace "`r`n", "`n"

        if ($existingNorm.Trim() -eq $newValueNorm.Trim()) {
            Write-Host "  Secret '$SecretName' unchanged, skipping." -ForegroundColor Gray
            return $false
        }

        Write-Host "  Updating '$SecretName' (value changed)..." -ForegroundColor Yellow
    }

    $azArgs = @("keyvault", "secret", "set", "--vault-name", $VaultName, "--name", $SecretName, "--file", $FilePath)
    if ($ContentType) {
        $azArgs += "--content-type"
        $azArgs += $ContentType
    }
    az @azArgs | Out-Null

    if ($existing) {
        Write-Host "  Updated '$SecretName'" -ForegroundColor Green
    } else {
        Write-Host "  Created '$SecretName'" -ForegroundColor Green
    }
    return $true
}

# ============================================================================
# Create Key Vaults if requested
# ============================================================================
if ($CreateVaults) {
    Write-Host "[0/9] Creating Key Vaults..." -ForegroundColor Yellow

    # Get current user's object ID for RBAC assignment
    $currentUser = az ad signed-in-user show --query id -o tsv 2>$null
    if (-not $currentUser) {
        Write-Host "  WARNING: Could not get current user ID for RBAC assignment" -ForegroundColor Yellow
    } else {
        Write-Host "  Current user ID: $currentUser" -ForegroundColor Gray
    }

    # Check if resource group exists
    $rgExists = az group show --name $ResourceGroup 2>$null
    if (-not $rgExists) {
        Write-Host "  Creating resource group '$ResourceGroup'..." -ForegroundColor Yellow
        az group create --name $ResourceGroup --location $Location | Out-Null
    }

    # Create Core vault
    $coreVaultExists = az keyvault show --name $CoreVaultName 2>$null
    if (-not $coreVaultExists) {
        Write-Host "  Creating Key Vault '$CoreVaultName'..." -ForegroundColor Yellow
        az keyvault create `
            --name $CoreVaultName `
            --resource-group $ResourceGroup `
            --location $Location `
            --enable-rbac-authorization true | Out-Null
        Write-Host "  Created '$CoreVaultName'" -ForegroundColor Green

        # Grant current user Key Vault Administrator role on Core vault
        if ($currentUser) {
            Write-Host "  Granting Key Vault Administrator to current user on '$CoreVaultName'..." -ForegroundColor Yellow
            az role assignment create `
                --assignee $currentUser `
                --role "Key Vault Administrator" `
                --scope "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$CoreVaultName" `
                2>$null | Out-Null
            Write-Host "  Granted Key Vault Administrator role" -ForegroundColor Green
        }
    } else {
        Write-Host "  Key Vault '$CoreVaultName' already exists." -ForegroundColor Gray
    }

    # Create OAuth vault
    $oauthVaultExists = az keyvault show --name $OAuthVaultName 2>$null
    if (-not $oauthVaultExists) {
        Write-Host "  Creating Key Vault '$OAuthVaultName'..." -ForegroundColor Yellow
        az keyvault create `
            --name $OAuthVaultName `
            --resource-group $ResourceGroup `
            --location $Location `
            --enable-rbac-authorization true | Out-Null
        Write-Host "  Created '$OAuthVaultName'" -ForegroundColor Green

        # Grant current user Key Vault Administrator role on OAuth vault
        if ($currentUser) {
            Write-Host "  Granting Key Vault Administrator to current user on '$OAuthVaultName'..." -ForegroundColor Yellow
            az role assignment create `
                --assignee $currentUser `
                --role "Key Vault Administrator" `
                --scope "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$OAuthVaultName" `
                2>$null | Out-Null
            Write-Host "  Granted Key Vault Administrator role" -ForegroundColor Green
        }
    } else {
        Write-Host "  Key Vault '$OAuthVaultName' already exists." -ForegroundColor Gray
    }

    Write-Host ""
}

# ============================================================================
# Grant Access to Current User (if requested)
# ============================================================================
if ($GrantAccess) {
    Write-Host "Granting Key Vault Administrator access to current user..." -ForegroundColor Yellow

    # Get current user's object ID
    $currentUser = az ad signed-in-user show --query id -o tsv 2>$null
    if (-not $currentUser) {
        Write-Host "  ERROR: Could not get current user ID" -ForegroundColor Red
    } else {
        Write-Host "  Current user ID: $currentUser" -ForegroundColor Gray

        # Grant on Core vault
        Write-Host "  Granting Key Vault Administrator on '$CoreVaultName'..." -ForegroundColor Yellow
        az role assignment create `
            --assignee $currentUser `
            --role "Key Vault Administrator" `
            --scope "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$CoreVaultName" `
            2>$null | Out-Null
        Write-Host "  Granted on '$CoreVaultName'" -ForegroundColor Green

        # Grant on OAuth vault
        Write-Host "  Granting Key Vault Administrator on '$OAuthVaultName'..." -ForegroundColor Yellow
        az role assignment create `
            --assignee $currentUser `
            --role "Key Vault Administrator" `
            --scope "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$OAuthVaultName" `
            2>$null | Out-Null
        Write-Host "  Granted on '$OAuthVaultName'" -ForegroundColor Green

        Write-Host ""
        Write-Host "  NOTE: RBAC role assignments may take up to 5 minutes to propagate." -ForegroundColor Cyan
        Write-Host "  If you still get authorization errors, wait a few minutes and try again." -ForegroundColor Cyan
    }

    Write-Host ""
}

# ============================================================================
# CORE VAULT SECRETS (Identity Service)
# ============================================================================
Write-Host "=== Core Vault ($CoreVaultName) - Identity Service ===" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# 1. OpenIddict Signing Certificate (EC P-256)
# ============================================================================
Write-Host "[1/9] Creating OpenIddict signing certificate (EC P-256)..." -ForegroundColor Yellow

$signingCertExists = az keyvault certificate show --vault-name $CoreVaultName --name "identity-signing-cert" 2>$null
if ($signingCertExists) {
    Write-Host "  Certificate 'identity-signing-cert' already exists, skipping." -ForegroundColor Gray
} else {
    $signingPolicy = @{
        issuerParameters = @{ name = "Self" }
        keyProperties = @{
            exportable = $true
            keyType = "EC"
            curve = "P-256"
            reuseKey = $false
        }
        x509CertificateProperties = @{
            subject = "CN=Meridian Console Identity Signing"
            validityInMonths = 24
            keyUsage = @("digitalSignature")
        }
    } | ConvertTo-Json -Depth 10 -Compress

    $policyFile = [System.IO.Path]::GetTempFileName()
    $signingPolicy | Out-File -FilePath $policyFile -Encoding utf8

    az keyvault certificate create `
        --vault-name $CoreVaultName `
        --name "identity-signing-cert" `
        --policy "@$policyFile" | Out-Null

    Remove-Item $policyFile
    Write-Host "  Created 'identity-signing-cert'" -ForegroundColor Green
}

# ============================================================================
# 2. OpenIddict Encryption Certificate (RSA 2048)
# ============================================================================
Write-Host ""
Write-Host "[2/9] Creating OpenIddict encryption certificate (RSA 2048)..." -ForegroundColor Yellow

$encryptionCertExists = az keyvault certificate show --vault-name $CoreVaultName --name "identity-encryption-cert" 2>$null
if ($encryptionCertExists) {
    Write-Host "  Certificate 'identity-encryption-cert' already exists, skipping." -ForegroundColor Gray
} else {
    $encryptionPolicy = @{
        issuerParameters = @{ name = "Self" }
        keyProperties = @{
            exportable = $true
            keyType = "RSA"
            keySize = 2048
            reuseKey = $false
        }
        x509CertificateProperties = @{
            subject = "CN=Meridian Console Identity Encryption"
            validityInMonths = 24
            keyUsage = @("keyEncipherment", "dataEncipherment")
        }
    } | ConvertTo-Json -Depth 10 -Compress

    $policyFile = [System.IO.Path]::GetTempFileName()
    $encryptionPolicy | Out-File -FilePath $policyFile -Encoding utf8

    az keyvault certificate create `
        --vault-name $CoreVaultName `
        --name "identity-encryption-cert" `
        --policy "@$policyFile" | Out-Null

    Remove-Item $policyFile
    Write-Host "  Created 'identity-encryption-cert'" -ForegroundColor Green
}

# ============================================================================
# 3. Exchange Token Public Key (for Identity service to validate BetterAuth tokens)
# ============================================================================
Write-Host ""
Write-Host "[3/9] Uploading exchange token public key..." -ForegroundColor Yellow

$exchangePublicKeyPath = Join-Path $PSScriptRoot "../../.secrets/exchange-public.pem"
if (Test-Path $exchangePublicKeyPath) {
    $resolvedPath = (Resolve-Path $exchangePublicKeyPath).Path
    Set-SecretFromFileIfNeeded -VaultName $CoreVaultName -SecretName "identity-exchange-public-key" -FilePath $resolvedPath -ContentType "application/x-pem-file"
} else {
    Write-Host "  ERROR: Exchange public key not found at $exchangePublicKeyPath" -ForegroundColor Red
}

# ============================================================================
# 4. JWT Signing Key (EC P-256 for JwtService)
# ============================================================================
Write-Host ""
Write-Host "[4/9] Creating JWT signing key (EC P-256)..." -ForegroundColor Yellow

# For generated keys, we only create if not exists (don't want to regenerate and break things)
$jwtKeyExists = az keyvault secret show --vault-name $CoreVaultName --name "identity-jwt-signing-key" 2>$null
if ($jwtKeyExists) {
    Write-Host "  Secret 'identity-jwt-signing-key' already exists, skipping." -ForegroundColor Gray
} else {
    # Generate EC P-256 private key using .NET cryptography (no OpenSSL required)
    try {
        # Create ECDsa with P-256 curve using OID
        $curve = [System.Security.Cryptography.ECCurve]::CreateFromValue("1.2.840.10045.3.1.7")  # OID for P-256/prime256v1
        $ecdsa = [System.Security.Cryptography.ECDsa]::Create($curve)

        $privateKeyBytes = $ecdsa.ExportPkcs8PrivateKey()
        $base64Key = [System.Convert]::ToBase64String($privateKeyBytes)

        # Format as PEM
        $pemLines = @("-----BEGIN PRIVATE KEY-----")
        for ($i = 0; $i -lt $base64Key.Length; $i += 64) {
            $pemLines += $base64Key.Substring($i, [Math]::Min(64, $base64Key.Length - $i))
        }
        $pemLines += "-----END PRIVATE KEY-----"
        $jwtKeyContent = $pemLines -join "`n"

        # Write to temp file and use helper function
        $tempFile = [System.IO.Path]::GetTempFileName()
        $jwtKeyContent | Out-File -FilePath $tempFile -Encoding utf8 -NoNewline

        Set-SecretFromFileIfNeeded -VaultName $CoreVaultName -SecretName "identity-jwt-signing-key" -FilePath $tempFile -ContentType "application/x-pem-file"

        Remove-Item $tempFile -ErrorAction SilentlyContinue
        $ecdsa.Dispose()
        Write-Host "  Created 'identity-jwt-signing-key'" -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: Failed to generate EC key: $_" -ForegroundColor Red
    }
}

# ============================================================================
# OAUTH VAULT SECRETS (Secrets Service)
# ============================================================================
Write-Host ""
Write-Host "=== OAuth Vault ($OAuthVaultName) - Secrets Service ===" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# 5. Exchange Token Private Key (for BetterAuth to sign tokens)
# ============================================================================
Write-Host "[5/9] Uploading exchange token private key..." -ForegroundColor Yellow

$exchangePrivateKeyPath = Join-Path $PSScriptRoot "../../.secrets/exchange-private-pkcs8.pem"
if (Test-Path $exchangePrivateKeyPath) {
    $resolvedPath = (Resolve-Path $exchangePrivateKeyPath).Path
    Set-SecretFromFileIfNeeded -VaultName $OAuthVaultName -SecretName "betterauth-exchange-private-key" -FilePath $resolvedPath -ContentType "application/x-pem-file"
} else {
    Write-Host "  ERROR: Exchange private key not found at $exchangePrivateKeyPath" -ForegroundColor Red
    Write-Host "  Run: openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out .secrets/exchange-private-pkcs8.pem" -ForegroundColor Yellow
}

# ============================================================================
# 6. BetterAuth Internal Secret
# ============================================================================
Write-Host ""
Write-Host "[6/9] Creating BetterAuth internal secret..." -ForegroundColor Yellow

# For generated secrets, we only create if not exists (don't want to regenerate and break things)
$betterAuthSecretExists = az keyvault secret show --vault-name $OAuthVaultName --name "betterauth-secret" 2>$null
if ($betterAuthSecretExists) {
    Write-Host "  Secret 'betterauth-secret' already exists, skipping." -ForegroundColor Gray
} else {
    $betterAuthSecret = -join ((1..32) | ForEach-Object { "{0:x2}" -f (Get-Random -Maximum 256) })
    Set-SecretIfNeeded -VaultName $OAuthVaultName -SecretName "betterauth-secret" -Value $betterAuthSecret
}

# ============================================================================
# 7. Infrastructure Secrets (PostgreSQL, RabbitMQ, Redis)
# ============================================================================
Write-Host ""
Write-Host "[7/9] Creating infrastructure secrets (if not exist)..." -ForegroundColor Yellow

# For generated passwords, we only create if not exists (don't want to regenerate and break things)
$infraSecrets = @("postgres-password", "rabbitmq-password", "redis-password")

foreach ($secretName in $infraSecrets) {
    $exists = az keyvault secret show --vault-name $OAuthVaultName --name $secretName 2>$null
    if ($exists) {
        Write-Host "  Secret '$secretName' already exists, skipping." -ForegroundColor Gray
    } else {
        $password = -join ((1..24) | ForEach-Object { "{0:x2}" -f (Get-Random -Maximum 256) })
        Set-SecretIfNeeded -VaultName $OAuthVaultName -SecretName $secretName -Value $password
    }
}

# ============================================================================
# 8. Better Auth Webhook Secret
# ============================================================================
Write-Host ""
Write-Host "[8/9] Creating webhook secret..." -ForegroundColor Yellow

# For generated secrets, we only create if not exists
$webhookSecretExists = az keyvault secret show --vault-name $OAuthVaultName --name "better-auth-webhook-secret" 2>$null
if ($webhookSecretExists) {
    Write-Host "  Secret 'better-auth-webhook-secret' already exists, skipping." -ForegroundColor Gray
} else {
    $webhookSecret = -join ((1..32) | ForEach-Object { "{0:x2}" -f (Get-Random -Maximum 256) })
    Set-SecretIfNeeded -VaultName $OAuthVaultName -SecretName "better-auth-webhook-secret" -Value $webhookSecret
}

# ============================================================================
# 9. OAuth Provider Placeholders
# ============================================================================
Write-Host ""
Write-Host "[9/9] Creating OAuth provider secret placeholders..." -ForegroundColor Yellow
Write-Host "  NOTE: You must update these with real values from each provider's developer portal." -ForegroundColor Cyan

$oauthSecrets = @(
    # Better Auth supported providers
    @{ Name = "oauth-facebook-app-id"; Provider = "Facebook" },
    @{ Name = "oauth-facebook-app-secret"; Provider = "Facebook" },
    @{ Name = "oauth-google-client-id"; Provider = "Google" },
    @{ Name = "oauth-google-client-secret"; Provider = "Google" },
    @{ Name = "oauth-discord-client-id"; Provider = "Discord" },
    @{ Name = "oauth-discord-client-secret"; Provider = "Discord" },
    @{ Name = "oauth-twitch-client-id"; Provider = "Twitch" },
    @{ Name = "oauth-twitch-client-secret"; Provider = "Twitch" },
    @{ Name = "oauth-github-client-id"; Provider = "GitHub" },
    @{ Name = "oauth-github-client-secret"; Provider = "GitHub" },
    @{ Name = "oauth-apple-client-id"; Provider = "Apple" },
    @{ Name = "oauth-apple-client-secret"; Provider = "Apple" },
    @{ Name = "oauth-amazon-client-id"; Provider = "Amazon" },
    @{ Name = "oauth-amazon-client-secret"; Provider = "Amazon" },
    # ASP.NET Identity providers (gaming platforms)
    @{ Name = "oauth-steam-api-key"; Provider = "Steam" },
    @{ Name = "oauth-battlenet-client-id"; Provider = "Battle.net" },
    @{ Name = "oauth-battlenet-client-secret"; Provider = "Battle.net" },
    @{ Name = "oauth-epic-client-id"; Provider = "Epic Games" },
    @{ Name = "oauth-epic-client-secret"; Provider = "Epic Games" },
    @{ Name = "oauth-xbox-client-id"; Provider = "Xbox/Microsoft" },
    @{ Name = "oauth-xbox-client-secret"; Provider = "Xbox/Microsoft" },
    # Microsoft OAuth (created by app registration script)
    @{ Name = "oauth-microsoft-personal-client-id"; Provider = "Microsoft Personal" },
    @{ Name = "oauth-microsoft-personal-client-secret"; Provider = "Microsoft Personal" },
    @{ Name = "oauth-microsoft-work-client-id"; Provider = "Microsoft Work/School" },
    @{ Name = "oauth-microsoft-work-client-secret"; Provider = "Microsoft Work/School" }
)

foreach ($secret in $oauthSecrets) {
    $existing = az keyvault secret show --vault-name $OAuthVaultName --name $secret.Name --query value -o tsv 2>$null
    if ($existing) {
        if ($existing -eq "PLACEHOLDER-UPDATE-ME") {
            Write-Host "  Secret '$($secret.Name)' is still a placeholder [$($secret.Provider)]" -ForegroundColor Yellow
        } else {
            Write-Host "  Secret '$($secret.Name)' already configured [$($secret.Provider)]" -ForegroundColor Gray
        }
    } else {
        Set-SecretIfNeeded -VaultName $OAuthVaultName -SecretName $secret.Name -Value "PLACEHOLDER-UPDATE-ME" | Out-Null
        Write-Host "  Created placeholder '$($secret.Name)' [$($secret.Provider)]" -ForegroundColor Yellow
    }
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-Host "=== Provisioning Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Vault Summary:" -ForegroundColor Cyan
Write-Host "  Core Vault ($CoreVaultName):" -ForegroundColor White
Write-Host "    - identity-signing-cert (OpenIddict signing)"
Write-Host "    - identity-encryption-cert (OpenIddict encryption)"
Write-Host "    - identity-exchange-public-key (BetterAuth token validation)"
Write-Host "    - identity-jwt-signing-key (JWT signing)"
Write-Host ""
Write-Host "  OAuth Vault ($OAuthVaultName):" -ForegroundColor White
Write-Host "    - betterauth-* (BetterAuth secrets)"
Write-Host "    - postgres/rabbitmq/redis passwords"
Write-Host "    - oauth-* (OAuth provider credentials)"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Update OAuth placeholders with real credentials from provider developer portals"
Write-Host "2. Grant Identity service access to '$CoreVaultName'"
Write-Host "3. Grant Secrets service access to '$OAuthVaultName'"
Write-Host "4. Update appsettings.json files with new vault URIs:"
Write-Host "   - Identity: https://$CoreVaultName.vault.azure.net/"
Write-Host "   - Secrets:  https://$OAuthVaultName.vault.azure.net/"
Write-Host ""
Write-Host "OAuth Provider Developer Portals:" -ForegroundColor Yellow
Write-Host "  Facebook:   https://developers.facebook.com/apps/"
Write-Host "  Google:     https://console.cloud.google.com/apis/credentials"
Write-Host "  Discord:    https://discord.com/developers/applications"
Write-Host "  Twitch:     https://dev.twitch.tv/console/apps"
Write-Host "  GitHub:     https://github.com/settings/developers"
Write-Host "  Apple:      https://developer.apple.com/account/resources/identifiers"
Write-Host "  Amazon:     https://developer.amazon.com/loginwithamazon/console"
Write-Host "  Microsoft:  https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps"
Write-Host "  Steam:      https://steamcommunity.com/dev/apikey"
Write-Host "  Battle.net: https://develop.battle.net/access/clients"
Write-Host ""
