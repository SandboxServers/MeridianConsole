# Secrets Service Implementation Plan

The CLI now has comprehensive secret and Key Vault management commands, but the **backend APIs are not yet implemented**. This document outlines what needs to be added to the Secrets service to support these features.

## Current State

### ✅ What's Implemented (Backend)
- **Read-only secret access** via `ISecretProvider`
- **Azure Key Vault integration** using official SDK (`Azure.Security.KeyVault.Secrets`)
- **Authentication** with `DefaultAzureCredential`
- **Caching** (5-minute TTL)
- **Permission-based access control** (secrets:read:oauth, etc.)
- **Allowlist-based security** (only whitelisted secrets accessible)

### ❌ What's Missing (Backend)
- Secret write operations (set, update, delete)
- Secret rotation capabilities
- Certificate management
- Key Vault CRUD operations (create, get, update, list vaults)
- Key Vault property management (soft delete, purge protection, SKU)

## Required Azure SDK Packages

Add to `Dhadgar.Secrets.csproj`:

```xml
<ItemGroup>
  <!-- Already have these -->
  <PackageReference Include="Azure.Identity" />
  <PackageReference Include="Azure.Security.KeyVault.Secrets" />

  <!-- Need to add these -->
  <PackageReference Include="Azure.Security.KeyVault.Certificates" />
  <PackageReference Include="Azure.ResourceManager.KeyVault" />
  <PackageReference Include="Azure.ResourceManager" />
</ItemGroup>
```

## API Endpoints to Implement

### Secret Write Operations

#### 1. Set/Update Secret
```http
PUT /api/v1/secrets/{secretName}
Content-Type: application/json

{
  "value": "string"
}
```

**Requirements:**
- Validate secret name is in allowlist
- Check permission: `secrets:write:{secretName}` or `secrets:write:{category}`
- Enforce 25KB size limit (25,600 bytes UTF-8)
- Use `SecretClient.SetSecretAsync()`
- Invalidate cache after write
- Return: `{ "name": "...", "updated": true }`

#### 2. Rotate Secret
```http
POST /api/v1/secrets/{secretName}/rotate
Content-Type: application/json

{}
```

**Requirements:**
- Check permission: `secrets:rotate:{secretName}`
- Generate new secret value (use `RandomNumberGenerator` for cryptographic randomness)
- Set new version in Key Vault
- Optionally keep old version valid for grace period
- Return: `{ "name": "...", "version": "...", "rotatedAt": "...", "expiresAt": "..." }`

**Implementation note:** Azure Key Vault automatically versions secrets. Use `SetSecretAsync()` to create new version, previous versions remain accessible by version ID.

### Certificate Management

#### 3. List Certificates
```http
GET /api/v1/certificates
GET /api/v1/keyvaults/{vaultName}/certificates
```

**Requirements:**
- Check permission: `secrets:read:certificates`
- Use `CertificateClient.GetPropertiesOfCertificatesAsync()`
- Return certificate metadata (name, subject, issuer, expiration, thumbprint)
- Filter by vault if specified
- Response:
```json
{
  "certificates": [
    {
      "name": "string",
      "subject": "string",
      "issuer": "string",
      "expiresAt": "datetime",
      "thumbprint": "string",
      "enabled": true
    }
  ]
}
```

#### 4. Import Certificate
```http
POST /api/v1/certificates
POST /api/v1/keyvaults/{vaultName}/certificates
Content-Type: application/json

{
  "name": "string",
  "certificateData": "base64-encoded certificate",
  "password": "string (optional)"
}
```

**Requirements:**
- Check permission: `secrets:write:certificates`
- Support formats: PFX, P12, PEM, CER
- Validate certificate format and password
- Use `CertificateClient.ImportCertificateAsync()`
- Return certificate metadata

**Size limits:**
- Certificate files: Practical limit ~1MB (API payload limits)
- Private keys: Must be in PKCS#12 (PFX/P12) with password

### Key Vault Management

#### 5. List Key Vaults
```http
GET /api/v1/keyvaults
```

**Requirements:**
- Check permission: `keyvault:read`
- Use `ArmClient` with `KeyVaultResource`
- List vaults in subscription
- Return: name, vaultUri, location, secretCount, enabled status

#### 6. Get Key Vault Details
```http
GET /api/v1/keyvaults/{vaultName}
```

**Requirements:**
- Check permission: `keyvault:read`
- Get vault properties, security settings, statistics
- Count secrets/keys/certificates
- Return detailed vault information (see CLI `VaultDetailResponse`)

#### 7. Create Key Vault
```http
POST /api/v1/keyvaults
Content-Type: application/json

{
  "name": "string",
  "location": "string"
}
```

**Requirements:**
- Check permission: `keyvault:write`
- Validate name (3-24 chars, alphanumeric + hyphens, globally unique)
- Use `ArmClient` to create vault
- Set default properties:
  - `enableSoftDelete`: true
  - `enablePurgeProtection`: true (recommended)
  - `softDeleteRetentionInDays`: 90
  - `enableRbacAuthorization`: true
  - `sku`: standard
- Return vault details

**Name validation:**
- Length: 3-24 characters
- Characters: letters, numbers, hyphens only
- Must be globally unique (DNS-resolvable: `{name}.vault.azure.net`)

#### 8. Update Key Vault Properties
```http
PATCH /api/v1/keyvaults/{vaultName}
Content-Type: application/json

{
  "enableSoftDelete": true,
  "enablePurgeProtection": true,
  "softDeleteRetentionDays": 90,
  "sku": "standard"
}
```

**Requirements:**
- Check permission: `keyvault:write`
- Update only provided properties (partial update)
- **CRITICAL:** Purge protection CANNOT be disabled once enabled
- Validate retention days: 7-90
- Validate SKU: "standard" or "premium"
- Use `KeyVaultResource.Update()`

**Important constraints:**
- `enablePurgeProtection`: Can be enabled but NEVER disabled (Azure enforces this)
- `softDeleteRetentionDays`: Must be 7-90 days
- `sku`: Downgrading from premium to standard may fail if premium features in use

## Permission Model Extensions

Add new permission claims to Identity service:

```
secrets:write:oauth          # Write OAuth secrets
secrets:write:betterauth     # Write BetterAuth secrets
secrets:write:infrastructure # Write infrastructure secrets
secrets:write:{secretName}   # Write specific secret
secrets:rotate:{secretName}  # Rotate specific secret
secrets:write:certificates   # Manage certificates
keyvault:read                # Read vault properties
keyvault:write               # Create/update vaults
```

## Implementation Order (Recommended)

1. **Phase 1: Secret Write Operations** (Easiest)
   - Add `SetSecretAsync` endpoint
   - Implement size validation (25KB)
   - Add cache invalidation
   - ~2-4 hours

2. **Phase 2: Certificate Management** (Medium)
   - Add `CertificateClient` to DI
   - Implement list/import endpoints
   - Handle certificate formats (PFX, PEM)
   - ~4-6 hours

3. **Phase 3: Secret Rotation** (Medium)
   - Implement rotation logic
   - Handle versioning strategy
   - Add grace period support
   - ~3-5 hours

4. **Phase 4: Key Vault Management** (Most Complex)
   - Add `Azure.ResourceManager.KeyVault` SDK
   - Implement ARM client with proper credentials
   - Add vault CRUD endpoints
   - Handle Azure subscription context
   - ~6-8 hours

## Azure Authentication Notes

### For Production
```csharp
// DefaultAzureCredential chain:
// 1. Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET)
// 2. Managed Identity (in Azure)
// 3. Visual Studio credential (local dev)
// 4. Azure CLI credential (local dev)

var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri(keyVaultUri), credential);
```

### For Local Development
Use Azure CLI authentication:
```bash
az login
az account set --subscription "subscription-id"
```

Or set environment variables:
```bash
export AZURE_TENANT_ID="..."
export AZURE_CLIENT_ID="..."
export AZURE_CLIENT_SECRET="..."
```

## Security Considerations

1. **Size Limits**
   - Secrets: 25KB (25,600 bytes) hard limit
   - Certificates: ~1MB practical limit
   - Reject oversized payloads early

2. **Rate Limiting**
   - Write operations should have stricter rate limits
   - Current read limits: 30-100 req/min
   - Suggested write limits: 10-20 req/min

3. **Audit Logging**
   - Log all write operations (set, rotate, delete)
   - Log certificate imports
   - Log vault creation/modification
   - Include user/org context

4. **Permissions**
   - Separate read/write permissions
   - Rotation requires special permission
   - Vault management requires elevated permission

## Testing Strategy

1. **Unit Tests**
   - Mock `SecretClient` and `CertificateClient`
   - Test permission validation
   - Test size limit enforcement
   - Test error handling

2. **Integration Tests**
   - Use Azure Key Vault emulator or dev vault
   - Test actual secret write/read cycle
   - Test certificate import/export
   - Test vault CRUD operations

3. **Manual Testing**
   - Use CLI commands against dev environment
   - Test with various certificate formats
   - Test error scenarios (oversized, invalid format)

## CLI Commands Already Built

All these CLI commands are **already implemented** and ready to use once backend APIs exist:

### Secret Management
```bash
dhadgar secret set <name> [value]
dhadgar secret set <name> --stdin
dhadgar secret rotate <name>
dhadgar secret list-certs
dhadgar secret import-cert <path>
```

### Key Vault Management
```bash
dhadgar keyvault list
dhadgar keyvault get <name>
dhadgar keyvault create <name> --location <location>
dhadgar keyvault update <name> [options]
```

## Next Steps

1. Add Azure SDK packages to `Dhadgar.Secrets.csproj`
2. Create new service interfaces (`ICertificateProvider`, `IKeyVaultManager`)
3. Implement endpoints in phases (start with secret write, then certificates, then vaults)
4. Add permission claims to Identity service
5. Add integration tests
6. Update API documentation (Swagger/OpenAPI)
7. Deploy and test with CLI

## References

- [Azure Key Vault Secrets SDK](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/security.keyvault.secrets-readme)
- [Azure Key Vault Certificates SDK](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/security.keyvault.certificates-readme)
- [Azure Resource Manager SDK](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/resourcemanager-readme)
- [Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
