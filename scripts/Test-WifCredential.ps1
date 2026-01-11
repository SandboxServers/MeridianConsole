#Requires -Version 7.0
<#
.SYNOPSIS
    Tests Workload Identity Federation (WIF) credential flow against the Identity service and Azure.

.DESCRIPTION
    This script:
    1. Obtains a WIF token from the Identity service
    2. Validates the token format and claims
    3. Exchanges the token with Azure AD for an Azure access token
    4. Optionally tests the Azure token against a target resource (e.g., Key Vault)

.PARAMETER IdentityUrl
    The URL of the Identity service token endpoint. Default: https://dev.meridianconsole.com/api/v1/identity/connect/token

.PARAMETER ClientId
    The client ID for the Identity service. Default: dev-client

.PARAMETER ClientSecret
    The client secret for the Identity service. Default: dev-secret

.PARAMETER AzureTenantId
    The Azure AD tenant ID. Default: 57eb34d8-1e4d-43d5-8d05-7292e5212ac2

.PARAMETER AzureClientId
    The Azure AD application (client) ID. Default: 003f2500-a5a7-4f6f-ac40-0e76f0cb38cf

.PARAMETER AzureScope
    The Azure scope to request. Default: https://vault.azure.net/.default

.PARAMETER SkipAzureExchange
    Skip the Azure token exchange step (useful for testing just the Identity service).

.EXAMPLE
    .\Test-WifCredential.ps1

.EXAMPLE
    .\Test-WifCredential.ps1 -IdentityUrl "http://localhost:5010/connect/token" -SkipAzureExchange

.EXAMPLE
    .\Test-WifCredential.ps1 -AzureScope "https://management.azure.com/.default"
#>

[CmdletBinding()]
param(
    [string]$IdentityUrl = "https://dev.meridianconsole.com/api/v1/identity/connect/token",
    [string]$ClientId = "dev-client",
    [string]$ClientSecret = "dev-secret",
    [string]$AzureTenantId = "57eb34d8-1e4d-43d5-8d05-7292e5212ac2",
    [string]$AzureClientId = "003f2500-a5a7-4f6f-ac40-0e76f0cb38cf",
    [string]$AzureScope = "https://vault.azure.net/.default",
    [string]$AzureVaultUri = "https://mc-core.vault.azure.net/",
    [switch]$SkipAzureExchange
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message, [string]$Status = "INFO")
    $color = switch ($Status) {
        "OK" { "Green" }
        "FAIL" { "Red" }
        "WARN" { "Yellow" }
        default { "Cyan" }
    }
    Write-Host "[$Status] " -ForegroundColor $color -NoNewline
    Write-Host $Message
}

function Decode-JwtPayload {
    param([string]$Token)

    $parts = $Token.Split('.')
    if ($parts.Length -ne 3) {
        throw "Invalid JWT format"
    }

    # Decode header
    $headerBase64 = $parts[0].Replace('-', '+').Replace('_', '/')
    switch ($headerBase64.Length % 4) {
        2 { $headerBase64 += "==" }
        3 { $headerBase64 += "=" }
    }
    $headerJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($headerBase64))
    $header = $headerJson | ConvertFrom-Json

    # Decode payload
    $payloadBase64 = $parts[1].Replace('-', '+').Replace('_', '/')
    switch ($payloadBase64.Length % 4) {
        2 { $payloadBase64 += "==" }
        3 { $payloadBase64 += "=" }
    }
    $payloadJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($payloadBase64))
    $payload = $payloadJson | ConvertFrom-Json

    return @{
        Header = $header
        Payload = $payload
        HeaderJson = $headerJson
        PayloadJson = $payloadJson
    }
}

# ============================================================================
# STEP 1: Get WIF Token from Identity Service
# ============================================================================

Write-Host ""
Write-Host "=" * 70 -ForegroundColor DarkGray
Write-Host "WORKLOAD IDENTITY FEDERATION (WIF) CREDENTIAL TEST" -ForegroundColor White
Write-Host "=" * 70 -ForegroundColor DarkGray
Write-Host ""

Write-Step "Step 1: Requesting WIF token from Identity service..."
Write-Host "  URL: $IdentityUrl" -ForegroundColor DarkGray
Write-Host "  Client: $ClientId" -ForegroundColor DarkGray

$tokenRequestBody = @{
    grant_type = "client_credentials"
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "wif"
}

try {
    $tokenResponse = Invoke-RestMethod -Uri $IdentityUrl -Method Post -Body $tokenRequestBody -ContentType "application/x-www-form-urlencoded"
    $wifToken = $tokenResponse.access_token

    if (-not $wifToken) {
        Write-Step "No access_token in response" "FAIL"
        Write-Host ($tokenResponse | ConvertTo-Json -Depth 5)
        exit 1
    }

    Write-Step "WIF token obtained successfully" "OK"
    Write-Host "  Token length: $($wifToken.Length) characters" -ForegroundColor DarkGray
    Write-Host "  Expires in: $($tokenResponse.expires_in) seconds" -ForegroundColor DarkGray
}
catch {
    Write-Step "Failed to get WIF token: $($_.Exception.Message)" "FAIL"
    if ($_.ErrorDetails.Message) {
        Write-Host "  Error details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    exit 1
}

# ============================================================================
# STEP 2: Validate WIF Token Format
# ============================================================================

Write-Host ""
Write-Step "Step 2: Validating WIF token format..."

try {
    $decoded = Decode-JwtPayload -Token $wifToken

    # Check typ header (must be "JWT" for Azure WIF)
    $typHeader = $decoded.Header.typ
    if ($typHeader -eq "JWT") {
        Write-Step "JWT typ header is correct: '$typHeader'" "OK"
    }
    elseif ($typHeader -eq "at+jwt") {
        Write-Step "JWT typ header is 'at+jwt' - Azure WIF will reject this!" "FAIL"
        Write-Host "  The AzureWifTokenHandler should have changed this to 'JWT'" -ForegroundColor Red
        exit 1
    }
    else {
        Write-Step "Unexpected typ header: '$typHeader'" "WARN"
    }

    # Display claims
    Write-Host ""
    Write-Host "  Token Header:" -ForegroundColor Cyan
    Write-Host "    alg: $($decoded.Header.alg)" -ForegroundColor DarkGray
    Write-Host "    typ: $($decoded.Header.typ)" -ForegroundColor DarkGray
    Write-Host "    kid: $($decoded.Header.kid)" -ForegroundColor DarkGray

    Write-Host ""
    Write-Host "  Token Claims:" -ForegroundColor Cyan
    Write-Host "    iss: $($decoded.Payload.iss)" -ForegroundColor DarkGray
    Write-Host "    sub: $($decoded.Payload.sub)" -ForegroundColor DarkGray
    Write-Host "    aud: $($decoded.Payload.aud)" -ForegroundColor DarkGray
    Write-Host "    scope: $($decoded.Payload.scope)" -ForegroundColor DarkGray

    # Check audience
    if ($decoded.Payload.aud -eq "api://AzureADTokenExchange") {
        Write-Step "Audience claim is correct for Azure WIF" "OK"
    }
    else {
        Write-Step "Unexpected audience: '$($decoded.Payload.aud)' (expected: api://AzureADTokenExchange)" "WARN"
    }

    # Check expiration
    $expTime = [DateTimeOffset]::FromUnixTimeSeconds($decoded.Payload.exp)
    $remaining = $expTime - [DateTimeOffset]::UtcNow
    Write-Host "    exp: $($expTime.ToString('yyyy-MM-dd HH:mm:ss')) UTC (in $([int]$remaining.TotalMinutes) minutes)" -ForegroundColor DarkGray
}
catch {
    Write-Step "Failed to decode token: $($_.Exception.Message)" "FAIL"
    exit 1
}

# ============================================================================
# STEP 3: Exchange WIF Token with Azure AD
# ============================================================================

if ($SkipAzureExchange) {
    Write-Host ""
    Write-Step "Skipping Azure token exchange (--SkipAzureExchange)" "INFO"
}
else {
    Write-Host ""
    Write-Step "Step 3: Exchanging WIF token with Azure AD..."
    Write-Host "  Tenant: $AzureTenantId" -ForegroundColor DarkGray
    Write-Host "  Client: $AzureClientId" -ForegroundColor DarkGray
    Write-Host "  Scope: $AzureScope" -ForegroundColor DarkGray

    $azureTokenUrl = "https://login.microsoftonline.com/$AzureTenantId/oauth2/v2.0/token"

    $azureRequestBody = @{
        client_id = $AzureClientId
        client_assertion_type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
        client_assertion = $wifToken
        scope = $AzureScope
        grant_type = "client_credentials"
    }

        try {
            $azureResponse = Invoke-RestMethod -Uri $azureTokenUrl -Method Post -Body $azureRequestBody -ContentType "application/x-www-form-urlencoded"
            $azureToken = $azureResponse.access_token

        if (-not $azureToken) {
            Write-Step "No access_token in Azure response" "FAIL"
            Write-Host ($azureResponse | ConvertTo-Json -Depth 5)
            exit 1
        }

        Write-Step "Azure token obtained successfully!" "OK"
        Write-Host "  Token length: $($azureToken.Length) characters" -ForegroundColor DarkGray
        Write-Host "  Expires in: $($azureResponse.expires_in) seconds" -ForegroundColor DarkGray
        Write-Host "  Token type: $($azureResponse.token_type)" -ForegroundColor DarkGray

        # Decode and show Azure token claims
        try {
            $azureDecoded = Decode-JwtPayload -Token $azureToken
            Write-Host ""
            Write-Host "  Azure Token Claims:" -ForegroundColor Cyan
            Write-Host "    iss: $($azureDecoded.Payload.iss)" -ForegroundColor DarkGray
            Write-Host "    aud: $($azureDecoded.Payload.aud)" -ForegroundColor DarkGray
            Write-Host "    azp: $($azureDecoded.Payload.azp)" -ForegroundColor DarkGray
            if ($azureDecoded.Payload.roles) {
                Write-Host "    roles: $($azureDecoded.Payload.roles -join ', ')" -ForegroundColor DarkGray
            }
        }
        catch {
            Write-Host "  (Could not decode Azure token claims)" -ForegroundColor DarkGray
        }

        # Step 4: Read secret from Core Key Vault using the Azure token
        Write-Host ""
        Write-Step "Step 4: Fetching Key Vault secret 'test'..."
        Write-Host "  Vault: $AzureVaultUri" -ForegroundColor DarkGray

        $secretName = "test"
        $vaultBuilder = [System.UriBuilder]::new($AzureVaultUri)
        $vaultBuilder.Path = "/secrets/$secretName"
        $vaultBuilder.Query = "api-version=7.4"
        $secretUrl = $vaultBuilder.Uri.AbsoluteUri

        try {
            $secretResponse = Invoke-RestMethod -Uri $secretUrl -Method Get -Headers @{
                Authorization = "Bearer $azureToken"
            }

            if ($null -eq $secretResponse.value) {
                Write-Step "Key Vault response missing secret value" "FAIL"
                Write-Host ($secretResponse | ConvertTo-Json -Depth 5)
                exit 1
            }

            Write-Step "Key Vault secret retrieved" "OK"
            Write-Host "  test: $($secretResponse.value)" -ForegroundColor DarkGray
        }
        catch {
            Write-Step "Failed to read Key Vault secret" "FAIL"
            if ($_.ErrorDetails.Message) {
                Write-Host "  Error details: $($_.ErrorDetails.Message)" -ForegroundColor Red
            }
            else {
                Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
            }
            exit 1
        }
    }
    catch {
        Write-Step "Azure token exchange failed" "FAIL"

        $errorMessage = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            try {
                $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
                Write-Host ""
                Write-Host "  Error: $($errorDetails.error)" -ForegroundColor Red
                Write-Host "  Description: $($errorDetails.error_description)" -ForegroundColor Red

                # Provide specific guidance based on error
                if ($errorDetails.error -eq "invalid_client") {
                    if ($errorDetails.error_description -match "AADSTS700211") {
                        Write-Host ""
                        Write-Host "  DIAGNOSIS: Federated credential issuer mismatch" -ForegroundColor Yellow
                        Write-Host "  The issuer in the token doesn't match the federated credential." -ForegroundColor Yellow
                        Write-Host "  Token issuer: $($decoded.Payload.iss)" -ForegroundColor Yellow
                        Write-Host ""
                        Write-Host "  FIX: Update the federated credential issuer to match:" -ForegroundColor Green
                        Write-Host "  az ad app federated-credential update \" -ForegroundColor Green
                        Write-Host "    --id <APP_OBJECT_ID> \" -ForegroundColor Green
                        Write-Host "    --federated-credential-id <CRED_ID> \" -ForegroundColor Green
                        Write-Host "    --parameters '{\"issuer\": \"$($decoded.Payload.iss)\"}'" -ForegroundColor Green
                    }
                    elseif ($errorDetails.error_description -match "AADSTS7000274") {
                        Write-Host ""
                        Write-Host "  DIAGNOSIS: Signature verification failed" -ForegroundColor Yellow
                        Write-Host "  Azure found the signing key but couldn't verify the signature." -ForegroundColor Yellow
                        Write-Host "  This usually means the JWT header was modified after signing." -ForegroundColor Yellow
                        Write-Host ""
                        Write-Host "  FIX: The AzureWifTokenHandler needs to use a different approach." -ForegroundColor Green
                        Write-Host "  Instead of post-modifying the token, configure OpenIddict to" -ForegroundColor Green
                        Write-Host "  generate tokens with typ:JWT from the start." -ForegroundColor Green
                    }
                    elseif ($errorDetails.error_description -match "AADSTS5002727") {
                        Write-Host ""
                        Write-Host "  DIAGNOSIS: JWT header type mismatch" -ForegroundColor Yellow
                        Write-Host "  Azure WIF requires typ:JWT but received: $typHeader" -ForegroundColor Yellow
                    }
                    elseif ($errorDetails.error_description -match "AADSTS501661") {
                        Write-Host ""
                        Write-Host "  DIAGNOSIS: OIDC endpoint unreachable" -ForegroundColor Yellow
                        Write-Host "  Azure couldn't reach the OIDC configuration or JWKS endpoint." -ForegroundColor Yellow
                        Write-Host ""
                        Write-Host "  Verify these URLs are publicly accessible:" -ForegroundColor Green
                        $issuerBase = $decoded.Payload.iss.TrimEnd('/')
                        Write-Host "  - $issuerBase/.well-known/openid-configuration" -ForegroundColor Green
                        Write-Host "  - $issuerBase/.well-known/jwks.json" -ForegroundColor Green
                    }
                }
            }
            catch {
                Write-Host "  Raw error: $($_.ErrorDetails.Message)" -ForegroundColor Red
            }
        }
        else {
            Write-Host "  $errorMessage" -ForegroundColor Red
        }
        exit 1
    }
}

# ============================================================================
# SUMMARY
# ============================================================================

Write-Host ""
Write-Host "=" * 70 -ForegroundColor DarkGray
Write-Step "WIF credential test completed successfully!" "OK"
Write-Host "=" * 70 -ForegroundColor DarkGray
Write-Host ""

# Output the tokens for further use
if (-not $SkipAzureExchange -and $azureToken) {
    Write-Host "To use the Azure token in subsequent commands:" -ForegroundColor Cyan
    Write-Host '$env:AZURE_TOKEN = "' -NoNewline -ForegroundColor DarkGray
    Write-Host ($azureToken.Substring(0, 50) + "...") -NoNewline -ForegroundColor DarkGray
    Write-Host '"' -ForegroundColor DarkGray
    Write-Host ""
}
