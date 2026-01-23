# Secrets Service API Reference

**Service**: Dhadgar.Secrets
**Base URL**: `/api/v1`
**Port**: 5110 (direct) or via Gateway at `/api/v1/secrets`
**Version**: 1.0
**Last Updated**: 2026-01-22

---

## Table of Contents

1. [Authentication](#authentication)
2. [Authorization](#authorization)
3. [Rate Limiting](#rate-limiting)
4. [Error Handling](#error-handling)
5. [Secrets Endpoints](#secrets-endpoints)
6. [Certificate Endpoints](#certificate-endpoints)
7. [Key Vault Endpoints](#key-vault-endpoints)

---

## Authentication

All endpoints require JWT Bearer authentication.

### Request Header

```http
Authorization: Bearer <jwt-token>
```

### Token Requirements

- **Issuer**: Must match configured `Auth:Issuer` (default: `https://meridianconsole.com/api/v1/identity`)
- **Audience**: Must match configured `Auth:Audience` (default: `meridian-api`)
- **Expiration**: Token must not be expired (60-second clock skew allowed)

### Example Token Claims

```json
{
  "sub": "user-guid-here",
  "iss": "https://meridianconsole.com/api/v1/identity",
  "aud": "meridian-api",
  "exp": 1737590400,
  "iat": 1737586800,
  "principal_type": "user",
  "permission": [
    "secrets:read:oauth",
    "secrets:write:oauth",
    "secrets:rotate:oauth"
  ]
}
```

---

## Authorization

### Permission Model

Permissions follow the format: `{resource}:{action}:{scope}`

| Permission | Description |
|------------|-------------|
| `secrets:*` | Full admin access to all secrets |
| `secrets:read:*` | Read any secret |
| `secrets:write:*` | Write any secret |
| `secrets:rotate:*` | Rotate any secret |
| `secrets:read:oauth` | Read OAuth category secrets |
| `secrets:read:betterauth` | Read BetterAuth category secrets |
| `secrets:read:infrastructure` | Read infrastructure secrets |
| `secrets:write:{category}` | Write secrets in category |
| `secrets:rotate:{category}` | Rotate secrets in category |
| `secrets:read:{secretName}` | Read specific secret |
| `secrets:write:{secretName}` | Write specific secret |
| `secrets:rotate:{secretName}` | Rotate specific secret |
| `secrets:read:certificates` | List/view certificates |
| `secrets:write:certificates` | Import/delete certificates |
| `keyvault:read` | List/view Key Vaults |
| `keyvault:write` | Create/update/delete Key Vaults |

### Permission Resolution Order

1. `secrets:*` (full admin)
2. `secrets:{action}:*` (action on all)
3. `secrets:{action}:{category}` (action on category)
4. `secrets:{action}:{secretName}` (action on specific secret)

### Break-Glass Access

Tokens with `break_glass: "true"` claim bypass normal permission checks. All break-glass access is logged at WARNING level.

Required claims for break-glass:
```json
{
  "break_glass": "true",
  "break_glass_reason": "INC-12345: Production outage"
}
```

---

## Rate Limiting

### Rate Limit Policies

| Policy | Limit | Window | Queue |
|--------|-------|--------|-------|
| SecretsRead | 100 requests | 1 minute | 10 |
| SecretsWrite | 20 requests | 1 minute | 5 |
| SecretsRotate | 5 requests | 1 minute | 2 |

### Rate Limit Response

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60
Content-Type: application/json

{
  "error": "Too many requests. Please try again later.",
  "retryAfterSeconds": 60
}
```

---

## Error Handling

All errors follow RFC 7807 Problem Details format.

### Error Response Format

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "Bad Request",
  "status": 400,
  "detail": "Secret name is required",
  "instance": "/api/v1/secrets/",
  "traceId": "00-abc123-def456-01"
}
```

### Common Error Codes

| Status | Description | Common Causes |
|--------|-------------|---------------|
| 400 | Bad Request | Invalid input, validation failure |
| 401 | Unauthorized | Missing or invalid token |
| 403 | Forbidden | Missing required permission |
| 404 | Not Found | Secret/certificate/vault not found |
| 409 | Conflict | Resource already exists |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Azure Key Vault error, service failure |

---

## Secrets Endpoints

### Get Secret

Retrieves a single secret by name.

```http
GET /api/v1/secrets/{secretName}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| secretName | string | Yes | Name of the secret (1-127 chars, alphanumeric + dashes) |

**Required Permission:** `secrets:read:{category}` or `secrets:read:{secretName}` or `secrets:read:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "oauth-discord-client-id",
  "value": "123456789012345678"
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Invalid secret name format |
| 403 | Secret not in allowed list or missing permission |
| 404 | Secret not found in Key Vault |

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/secrets/oauth-discord-client-id"
```

---

### Get Secrets Batch

Retrieves multiple secrets in a single request.

```http
POST /api/v1/secrets/batch
```

**Request Body:**

```json
{
  "secretNames": ["oauth-discord-client-id", "oauth-discord-client-secret", "oauth-github-client-id"]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| secretNames | string[] | Yes | Array of secret names to retrieve |

**Required Permission:** Per-secret permissions are checked individually. Only authorized secrets are returned.

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "secrets": {
    "oauth-discord-client-id": "123456789012345678",
    "oauth-discord-client-secret": "abcd1234efgh5678"
  }
}
```

**Notes:**
- Secrets without permission are silently omitted from response
- Returns 403 only if ALL requested secrets are denied
- Maximum 50 secrets per request (recommended)

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Empty secretNames array or invalid name format |
| 403 | No permissions for any requested secrets |

**Example:**

```bash
curl -X POST \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"secretNames": ["oauth-discord-client-id", "oauth-discord-client-secret"]}' \
  "https://api.meridianconsole.com/api/v1/secrets/batch"
```

---

### Get OAuth Secrets

Retrieves all configured OAuth provider secrets.

```http
GET /api/v1/secrets/oauth
```

**Required Permission:** `secrets:read:oauth` or `secrets:read:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "secrets": {
    "oauth-discord-client-id": "123456789012345678",
    "oauth-discord-client-secret": "abcd1234efgh5678",
    "oauth-github-client-id": "Iv1.abc123def456",
    "oauth-github-client-secret": "secret123"
  }
}
```

**Notes:**
- Only returns secrets that exist in Key Vault
- Placeholder values (`PLACEHOLDER-UPDATE-ME`) are excluded

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/secrets/oauth"
```

---

### Get BetterAuth Secrets

Retrieves secrets required by the BetterAuth service.

```http
GET /api/v1/secrets/betterauth
```

**Required Permission:** `secrets:read:betterauth` or `secrets:read:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "secrets": {
    "betterauth-secret": "base64-encoded-secret",
    "betterauth-exchange-private-key": "-----BEGIN PRIVATE KEY-----\n..."
  }
}
```

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/secrets/betterauth"
```

---

### Get Infrastructure Secrets

Retrieves infrastructure secrets (database, messaging passwords).

```http
GET /api/v1/secrets/infrastructure
```

**Required Permission:** `secrets:read:infrastructure` or `secrets:read:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "secrets": {
    "postgres-password": "database-password-here",
    "rabbitmq-password": "mq-password-here",
    "redis-password": "cache-password-here"
  }
}
```

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/secrets/infrastructure"
```

---

### Set Secret

Creates or updates a secret value.

```http
PUT /api/v1/secrets/{secretName}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| secretName | string | Yes | Name of the secret |

**Request Body:**

```json
{
  "value": "new-secret-value"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| value | string | Yes | Secret value (max 25,600 bytes UTF-8) |

**Required Permission:** `secrets:write:{category}` or `secrets:write:{secretName}` or `secrets:write:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "oauth-discord-client-secret",
  "updated": true
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Empty value, invalid name, or value exceeds 25KB |
| 403 | Secret not in allowed list or missing permission |

**Example:**

```bash
curl -X PUT \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"value": "new-secret-value"}' \
  "https://api.meridianconsole.com/api/v1/secrets/oauth-discord-client-secret"
```

---

### Rotate Secret

Generates a new cryptographically secure value for a secret.

```http
POST /api/v1/secrets/{secretName}/rotate
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| secretName | string | Yes | Name of the secret to rotate |

**Request Body:** None required (empty body or `{}`)

**Required Permission:** `secrets:rotate:{category}` or `secrets:rotate:{secretName}` or `secrets:rotate:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "oauth-discord-client-secret",
  "version": "abc123def456789",
  "rotatedAt": "2026-01-22T15:30:00Z",
  "expiresAt": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| name | string | Secret name |
| version | string | New version identifier from Key Vault |
| rotatedAt | datetime | UTC timestamp of rotation |
| expiresAt | datetime? | Expiration time (null if no expiry set) |

**Notes:**
- Generates 32 bytes (256 bits) of cryptographic random data
- Value is Base64 encoded
- Previous versions remain accessible by version ID in Key Vault
- Cache is invalidated immediately

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Invalid secret name format |
| 403 | Secret not in allowed list or missing permission |
| 404 | Secret not found (cannot rotate non-existent secret) |

**Example:**

```bash
curl -X POST \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/secrets/oauth-discord-client-secret/rotate"
```

---

### Delete Secret

Deletes a secret (soft delete if vault has soft delete enabled).

```http
DELETE /api/v1/secrets/{secretName}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| secretName | string | Yes | Name of the secret to delete |

**Required Permission:** `secrets:write:{category}` or `secrets:write:{secretName}` or `secrets:write:*` or `secrets:*`

**Response:**

```http
HTTP/1.1 204 No Content
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Invalid secret name format |
| 403 | Secret not in allowed list or missing permission |
| 404 | Secret not found |

**Notes:**
- If vault has soft delete enabled, secret can be recovered within retention period
- Cache is invalidated immediately

**Example:**

```bash
curl -X DELETE \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/secrets/oauth-old-provider-secret"
```

---

## Certificate Endpoints

### List Certificates

Lists all certificates in the default Key Vault.

```http
GET /api/v1/certificates
```

**Required Permission:** `secrets:read:certificates`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "certificates": [
    {
      "name": "oidc-signing-cert",
      "subject": "CN=Meridian Identity Signing",
      "issuer": "CN=Meridian Identity Signing",
      "expiresAt": "2027-01-22T00:00:00Z",
      "thumbprint": "A1B2C3D4E5F6...",
      "enabled": true
    },
    {
      "name": "oidc-encryption-cert",
      "subject": "CN=Meridian Identity Encryption",
      "issuer": "CN=Meridian Identity Encryption",
      "expiresAt": "2027-01-22T00:00:00Z",
      "thumbprint": "F6E5D4C3B2A1...",
      "enabled": true
    }
  ]
}
```

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/certificates"
```

---

### List Vault Certificates

Lists certificates in a specific Key Vault.

```http
GET /api/v1/keyvaults/{vaultName}/certificates
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| vaultName | string | Yes | Name of the Key Vault |

**Required Permission:** `secrets:read:certificates`

**Response:** Same as [List Certificates](#list-certificates)

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/keyvaults/mc-oauth/certificates"
```

---

### Import Certificate

Imports a certificate to the default Key Vault.

```http
POST /api/v1/certificates
```

**Request Body:**

```json
{
  "name": "my-certificate",
  "certificateData": "MIIKQQIBAzCCCf0GCSqGSIb3DQEHAaCCCe4EggnqMIIJ5jCCBg8GCSqGSIb3DQEHAaCCBgAEggX8MIIF+DCCBfQGCyqGSIb3DQEMCgECoIIE/jCCBPowHAYKKoZIhvcNAQwBAzAOBAhS...",
  "password": "pfx-password"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| name | string | Yes | Certificate name (1-127 chars) |
| certificateData | string | Yes | Base64-encoded certificate (PFX/P12) |
| password | string | No | Password for encrypted PFX |

**Required Permission:** `secrets:write:certificates`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "my-certificate",
  "subject": "CN=My Service",
  "issuer": "CN=My CA",
  "thumbprint": "A1B2C3D4E5F6...",
  "expiresAt": "2027-01-22T00:00:00Z"
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Invalid base64, wrong password, or invalid certificate format |
| 403 | Missing permission |
| 409 | Certificate with same name already exists |

**Example:**

```bash
# First, base64 encode your PFX file
CERT_DATA=$(base64 -w 0 my-certificate.pfx)

curl -X POST \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d "{\"name\": \"my-certificate\", \"certificateData\": \"$CERT_DATA\", \"password\": \"pfx-password\"}" \
  "https://api.meridianconsole.com/api/v1/certificates"
```

---

### Import Vault Certificate

Imports a certificate to a specific Key Vault.

```http
POST /api/v1/keyvaults/{vaultName}/certificates
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| vaultName | string | Yes | Name of the Key Vault |

**Request Body:** Same as [Import Certificate](#import-certificate)

**Required Permission:** `secrets:write:certificates`

**Example:**

```bash
curl -X POST \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"name": "my-certificate", "certificateData": "...", "password": "..."}' \
  "https://api.meridianconsole.com/api/v1/keyvaults/mc-oauth/certificates"
```

---

### Delete Certificate

Deletes a certificate from the default Key Vault.

```http
DELETE /api/v1/certificates/{name}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| name | string | Yes | Certificate name |

**Required Permission:** `secrets:write:certificates`

**Response:**

```http
HTTP/1.1 204 No Content
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 403 | Missing permission |
| 404 | Certificate not found |

**Example:**

```bash
curl -X DELETE \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/certificates/old-certificate"
```

---

## Key Vault Endpoints

### List Key Vaults

Lists all Key Vaults in the subscription.

```http
GET /api/v1/keyvaults
```

**Required Permission:** `keyvault:read`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "vaults": [
    {
      "name": "mc-oauth",
      "vaultUri": "https://mc-oauth.vault.azure.net/",
      "location": "eastus",
      "secretCount": 42,
      "enabled": true
    },
    {
      "name": "mc-infrastructure",
      "vaultUri": "https://mc-infrastructure.vault.azure.net/",
      "location": "eastus",
      "secretCount": 8,
      "enabled": true
    }
  ]
}
```

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/keyvaults"
```

---

### Get Key Vault Details

Gets detailed information about a specific Key Vault.

```http
GET /api/v1/keyvaults/{vaultName}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| vaultName | string | Yes | Name of the Key Vault |

**Required Permission:** `keyvault:read`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "mc-oauth",
  "vaultUri": "https://mc-oauth.vault.azure.net/",
  "location": "eastus",
  "resourceGroup": "meridian-rg",
  "sku": "standard",
  "tenantId": "abc123-...",
  "enableSoftDelete": true,
  "enablePurgeProtection": true,
  "softDeleteRetentionDays": 90,
  "enableRbacAuthorization": true,
  "publicNetworkAccess": "Enabled",
  "secretCount": 42,
  "keyCount": 2,
  "certificateCount": 3,
  "createdAt": "2025-06-15T10:30:00Z",
  "updatedAt": "2026-01-20T14:45:00Z"
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 403 | Missing permission |
| 404 | Vault not found |

**Example:**

```bash
curl -X GET \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/keyvaults/mc-oauth"
```

---

### Create Key Vault

Creates a new Key Vault.

```http
POST /api/v1/keyvaults
```

**Request Body:**

```json
{
  "name": "new-vault",
  "location": "eastus",
  "resourceGroupName": "meridian-rg"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| name | string | Yes | Vault name (3-24 chars, alphanumeric + hyphens, globally unique) |
| location | string | Yes | Azure region (e.g., "eastus", "westus2") |
| resourceGroupName | string | No | Resource group (uses default if not specified) |

**Required Permission:** `keyvault:write`

**Response:**

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "new-vault",
  "vaultUri": "https://new-vault.vault.azure.net/",
  "location": "eastus",
  "resourceGroup": "meridian-rg",
  "sku": "standard",
  "tenantId": "abc123-...",
  "enableSoftDelete": true,
  "enablePurgeProtection": true,
  "softDeleteRetentionDays": 90,
  "enableRbacAuthorization": true,
  "publicNetworkAccess": "Enabled",
  "secretCount": 0,
  "keyCount": 0,
  "certificateCount": 0,
  "createdAt": "2026-01-22T15:30:00Z",
  "updatedAt": "2026-01-22T15:30:00Z"
}
```

**Default Settings Applied:**

| Property | Default Value |
|----------|---------------|
| enableSoftDelete | true |
| enablePurgeProtection | true |
| softDeleteRetentionDays | 90 |
| enableRbacAuthorization | true |
| sku | standard |

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Invalid name format or location |
| 403 | Missing permission |
| 409 | Vault with same name already exists |

**Example:**

```bash
curl -X POST \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"name": "new-vault", "location": "eastus"}' \
  "https://api.meridianconsole.com/api/v1/keyvaults"
```

---

### Update Key Vault

Updates Key Vault properties.

```http
PATCH /api/v1/keyvaults/{vaultName}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| vaultName | string | Yes | Name of the Key Vault |

**Request Body:**

```json
{
  "enableSoftDelete": true,
  "enablePurgeProtection": true,
  "softDeleteRetentionDays": 90,
  "sku": "premium"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| enableSoftDelete | boolean | No | Enable soft delete |
| enablePurgeProtection | boolean | No | Enable purge protection (cannot be disabled once enabled) |
| softDeleteRetentionDays | integer | No | Retention period (7-90 days) |
| sku | string | No | "standard" or "premium" |

**Required Permission:** `keyvault:write`

**Response:** Same as [Get Key Vault Details](#get-key-vault-details)

**Important Constraints:**

- `enablePurgeProtection` can only be enabled, **never disabled**
- `softDeleteRetentionDays` must be between 7 and 90
- Downgrading `sku` from "premium" to "standard" may fail if premium features are in use

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Invalid property values |
| 403 | Missing permission |
| 404 | Vault not found |

**Example:**

```bash
curl -X PATCH \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"softDeleteRetentionDays": 30}' \
  "https://api.meridianconsole.com/api/v1/keyvaults/mc-oauth"
```

---

### Delete Key Vault

Deletes a Key Vault (soft delete if enabled).

```http
DELETE /api/v1/keyvaults/{vaultName}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| vaultName | string | Yes | Name of the Key Vault |

**Required Permission:** `keyvault:write`

**Response:**

```http
HTTP/1.1 204 No Content
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 403 | Missing permission |
| 404 | Vault not found |

**Notes:**
- If soft delete is enabled, vault can be recovered within retention period
- To permanently delete, use Azure CLI: `az keyvault purge --name <vault-name>`

**Example:**

```bash
curl -X DELETE \
  -H "Authorization: Bearer <token>" \
  "https://api.meridianconsole.com/api/v1/keyvaults/old-vault"
```

---

## Appendix: Allowed Secret Names

The service only dispenses secrets from the configured allowlist. Default categories:

### OAuth Secrets

```
oauth-amazon-client-id, oauth-amazon-client-secret
oauth-battlenet-client-id, oauth-battlenet-client-secret
oauth-discord-client-id, oauth-discord-client-secret
oauth-facebook-app-id, oauth-facebook-app-secret
oauth-github-client-id, oauth-github-client-secret
oauth-google-client-id, oauth-google-client-secret
oauth-lego-client-id, oauth-lego-client-secret
oauth-microsoft-client-id
oauth-microsoft-personal-client-id, oauth-microsoft-personal-client-secret
oauth-microsoft-work-client-id, oauth-microsoft-work-client-secret
oauth-paypal-client-id, oauth-paypal-client-secret
oauth-reddit-client-id, oauth-reddit-client-secret
oauth-roblox-client-id, oauth-roblox-client-secret
oauth-slack-client-id, oauth-slack-client-secret
oauth-spotify-client-id, oauth-spotify-client-secret
oauth-steam-api-key
oauth-twitch-client-id, oauth-twitch-client-secret
oauth-xbox-client-id, oauth-xbox-client-secret
oauth-yahoo-client-id, oauth-yahoo-client-secret
```

### BetterAuth Secrets

```
betterauth-secret
betterauth-exchange-private-key
better-auth-webhook-secret
```

### Infrastructure Secrets

```
postgres-password
rabbitmq-password
redis-password
```

---

## Appendix: Secret Name Validation Rules

Secret names must comply with Azure Key Vault naming rules:

| Rule | Requirement |
|------|-------------|
| Length | 1-127 characters |
| Characters | Alphanumeric (a-z, A-Z, 0-9) and dashes (-) |
| Start/End | Must start and end with alphanumeric character |
| Reserved | Cannot contain `..`, `/`, `\`, or null bytes |

**Valid Examples:**
- `oauth-discord-client-id`
- `my-secret-123`
- `a`

**Invalid Examples:**
- `-starts-with-dash`
- `ends-with-dash-`
- `has..double-dots`
- `has/slash`
- `has spaces`

---

**End of API Reference**
