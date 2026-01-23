# Dhadgar.Secrets Service

A secure, production-ready secrets management microservice for the Meridian Console platform. This service acts as a controlled gateway to Azure Key Vault, providing secrets dispensing with fine-grained authorization, comprehensive audit logging, rate limiting, and support for multiple authentication mechanisms including Workload Identity Federation (WIF).

## Table of Contents

1. [Overview](#overview)
2. [Tech Stack](#tech-stack)
3. [Quick Start](#quick-start)
4. [Configuration](#configuration)
5. [Architecture](#architecture)
6. [Authorization System](#authorization-system)
7. [Audit Logging](#audit-logging)
8. [Rate Limiting](#rate-limiting)
9. [API Endpoints](#api-endpoints)
10. [Break-Glass Access](#break-glass-access)
11. [Service Accounts vs Users](#service-accounts-vs-users)
12. [Azure Key Vault Integration](#azure-key-vault-integration)
13. [Development Provider](#development-provider)
14. [Testing](#testing)
15. [Security Considerations](#security-considerations)
16. [Related Documentation](#related-documentation)

---

## Overview

### Purpose

The Secrets service is a **secrets dispensary** within the Meridian Console ecosystem. Unlike direct Key Vault access, this service:

- **Controls which secrets can be dispensed** via an explicit allowlist
- **Enforces fine-grained authorization** using JWT claims and permission hierarchies
- **Provides comprehensive audit logging** for compliance and security monitoring
- **Implements rate limiting** to prevent abuse and runaway service consumption
- **Supports emergency break-glass access** for incident response
- **Differentiates between service accounts and human users** in audit trails

### Design Philosophy

The Identity service maintains direct Azure Key Vault access for its core secrets (JWT signing keys, etc.). The Secrets service, however, dispenses **application-level secrets** like:

- OAuth provider credentials (Discord, Steam, GitHub, Google, etc.)
- BetterAuth configuration secrets
- Infrastructure credentials (PostgreSQL, RabbitMQ, Redis passwords)

This separation ensures the Identity service remains autonomous while other services retrieve their secrets through a controlled, audited channel.

### Key Features

| Feature | Description |
|---------|-------------|
| **Allowlist-Based Dispensing** | Only explicitly allowed secrets can be retrieved |
| **Permission Hierarchy** | Supports wildcards, categories, and specific secret permissions |
| **Break-Glass Access** | Emergency access with mandatory audit logging |
| **Multi-Provider Support** | Azure Key Vault (production) or in-memory (development) |
| **Workload Identity Federation** | Passwordless Azure authentication for containerized workloads |
| **Certificate Management** | Import, list, and delete certificates in Key Vault |
| **Key Vault CRUD** | Full lifecycle management of Azure Key Vaults |
| **Rate Limiting** | Separate limits for read, write, and rotate operations |
| **Health Checks** | Kubernetes-ready readiness probes |

---

## Tech Stack

### Core Framework

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime platform |
| ASP.NET Core | 10.0 | Web API framework |
| Minimal APIs | - | Endpoint definition pattern |

### Azure Integration

| Package | Purpose |
|---------|---------|
| `Azure.Identity` | Credential management (DefaultAzureCredential, ClientAssertionCredential) |
| `Azure.Security.KeyVault.Secrets` | Secret CRUD operations |
| `Azure.Security.KeyVault.Certificates` | Certificate management |
| `Azure.Security.KeyVault.Keys` | Key counting for vault statistics |
| `Azure.ResourceManager` | ARM client for subscription-level operations |
| `Azure.ResourceManager.KeyVault` | Key Vault resource management |

### Authentication & Authorization

| Package | Purpose |
|---------|---------|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT token validation |
| `Microsoft.IdentityModel.Tokens` | Token validation parameters |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | OIDC metadata retrieval |

### Observability

| Package | Purpose |
|---------|---------|
| `OpenTelemetry.Extensions.Hosting` | OTel integration |
| `OpenTelemetry.Instrumentation.AspNetCore` | HTTP request tracing |
| `OpenTelemetry.Instrumentation.Http` | Outbound HTTP tracing |
| `OpenTelemetry.Instrumentation.Runtime` | Runtime metrics |
| `OpenTelemetry.Instrumentation.Process` | Process metrics |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP export |

### Messaging (Planned)

| Package | Purpose |
|---------|---------|
| `MassTransit` | Message bus abstraction |
| `MassTransit.RabbitMQ` | RabbitMQ transport |

### API Documentation

| Package | Purpose |
|---------|---------|
| `Swashbuckle.AspNetCore` | Swagger/OpenAPI generation |
| `Microsoft.AspNetCore.OpenApi` | OpenAPI support |

### Shared Libraries

| Project | Purpose |
|---------|---------|
| `Dhadgar.Contracts` | Shared DTOs and message contracts |
| `Dhadgar.Shared` | Common utilities |
| `Dhadgar.Messaging` | MassTransit conventions |
| `Dhadgar.ServiceDefaults` | Common middleware (correlation, problem details, request logging) |

---

## Quick Start

### Prerequisites

- .NET SDK 10.0.100+
- Docker (for local infrastructure)
- Azure Key Vault (for production) OR development mode enabled

### Running Locally (Development Mode)

1. **Start local infrastructure** (if using Key Vault):
   ```bash
   docker compose -f deploy/compose/docker-compose.dev.yml up -d
   ```

2. **Configure development secrets** (create `appsettings.Development.json`):
   ```json
   {
     "Secrets": {
       "UseDevelopmentProvider": true,
       "Development": {
         "Secrets": {
           "oauth-discord-client-id": "your-discord-client-id",
           "oauth-discord-client-secret": "your-discord-client-secret",
           "postgres-password": "dhadgar"
         }
       }
     }
   }
   ```

3. **Run the service**:
   ```bash
   dotnet run --project src/Dhadgar.Secrets
   ```

4. **Access the service**:
   - API: http://localhost:5011
   - Swagger UI: http://localhost:5011/swagger (Development mode only)
   - Health check: http://localhost:5011/healthz

### Running with Azure Key Vault

1. **Configure Key Vault URI** via user secrets:
   ```bash
   dotnet user-secrets init --project src/Dhadgar.Secrets
   dotnet user-secrets set "Secrets:KeyVaultUri" "https://your-vault.vault.azure.net/" --project src/Dhadgar.Secrets
   ```

2. **Ensure Azure authentication** (one of):
   - Azure CLI: `az login`
   - Environment variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`
   - Managed Identity (in Azure)
   - Workload Identity Federation (in Kubernetes)

3. **Run the service**:
   ```bash
   dotnet run --project src/Dhadgar.Secrets
   ```

### Verifying the Service

```bash
# Check service is running
curl http://localhost:5011/hello
# Response: Hello from Dhadgar.Secrets

# Check health
curl http://localhost:5011/healthz
# Response: {"status":"Healthy",...}

# Access Swagger (development only)
open http://localhost:5011/swagger
```

---

## Configuration

### Configuration Sources (Priority Order)

1. `appsettings.json` - Base configuration
2. `appsettings.Development.json` - Development overrides
3. Environment variables
4. User secrets (`dotnet user-secrets`)
5. Kubernetes ConfigMaps/Secrets (production)

### Core Configuration Options

#### `appsettings.json` Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Auth": {
    "Issuer": "https://meridianconsole.com/api/v1/identity",
    "Audience": "meridian-api",
    "ClockSkewSeconds": 30,
    "MetadataAddress": "https://dev.meridianconsole.com/api/v1/identity/.well-known/openid-configuration",
    "InternalBaseUrl": "http://identity:8080"
  },
  "Secrets": {
    "KeyVaultUri": "https://your-vault.vault.azure.net/",
    "AzureSubscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "UseDevelopmentProvider": false,
    "Wif": {
      "TenantId": "your-azure-tenant-id",
      "ClientId": "your-app-registration-client-id",
      "IdentityTokenEndpoint": "http://identity:8080/connect/token",
      "ServiceClientId": "secrets-service",
      "ServiceClientSecret": "service-secret"
    },
    "Permissions": {
      "OAuthRead": "secrets:read:oauth",
      "BetterAuthRead": "secrets:read:betterauth",
      "InfrastructureRead": "secrets:read:infrastructure"
    },
    "AllowedSecrets": {
      "OAuth": [
        "oauth-discord-client-id",
        "oauth-discord-client-secret",
        "oauth-github-client-id",
        "oauth-github-client-secret",
        "oauth-google-client-id",
        "oauth-google-client-secret",
        "oauth-steam-api-key"
      ],
      "BetterAuth": [
        "betterauth-secret",
        "betterauth-exchange-private-key",
        "better-auth-webhook-secret"
      ],
      "Infrastructure": [
        "postgres-password",
        "rabbitmq-password",
        "redis-password"
      ]
    }
  },
  "Readiness": {
    "ProbeSecretName": "",
    "CheckCertificates": true
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Configuration Sections Explained

#### `Auth` Section

| Key | Description | Example |
|-----|-------------|---------|
| `Issuer` | JWT token issuer (must match `iss` claim) | `https://meridianconsole.com/api/v1/identity` |
| `Audience` | Expected JWT audience | `meridian-api` |
| `ClockSkewSeconds` | Allowed clock drift for token validation | `30` |
| `MetadataAddress` | OIDC discovery endpoint (optional, derived from Issuer if omitted) | See above |
| `InternalBaseUrl` | Internal service URL for JWKS retrieval in Docker/K8s | `http://identity:8080` |

The `InternalBaseUrl` enables a URL-rewriting document retriever that fetches JWKS from internal URLs while validating tokens with external issuer URLs. This is essential for containerized environments where services communicate internally but tokens contain external URLs.

#### `Secrets` Section

| Key | Description | Default |
|-----|-------------|---------|
| `KeyVaultUri` | Azure Key Vault URI | Required for production |
| `AzureSubscriptionId` | Azure subscription for vault management | From `AZURE_SUBSCRIPTION_ID` env var |
| `UseDevelopmentProvider` | Use in-memory provider instead of Key Vault | `false` |
| `Wif` | Workload Identity Federation configuration | See below |
| `Permissions` | Permission name mappings | See below |
| `AllowedSecrets` | Allowlist of dispensable secrets by category | See below |

#### `Secrets:Wif` Section (Workload Identity Federation)

| Key | Description | Example |
|-----|-------------|---------|
| `TenantId` | Azure AD tenant ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `ClientId` | App registration client ID in Azure | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `IdentityTokenEndpoint` | Internal Identity service token endpoint | `http://identity:8080/connect/token` |
| `ServiceClientId` | Client ID for Identity service authentication | `secrets-service` |
| `ServiceClientSecret` | Client secret for Identity service | `service-secret` |

When WIF is configured, the service obtains a JWT from the Identity service and uses it as a client assertion to authenticate to Azure AD without storing Azure client secrets.

#### `Secrets:AllowedSecrets` Section

This is the **security-critical allowlist** that determines which secrets can be dispensed:

```json
{
  "AllowedSecrets": {
    "OAuth": [
      "oauth-amazon-client-id",
      "oauth-amazon-client-secret",
      "oauth-battlenet-client-id",
      "oauth-battlenet-client-secret",
      "oauth-discord-client-id",
      "oauth-discord-client-secret",
      "oauth-facebook-app-id",
      "oauth-facebook-app-secret",
      "oauth-github-client-id",
      "oauth-github-client-secret",
      "oauth-google-client-id",
      "oauth-google-client-secret",
      "oauth-lego-client-id",
      "oauth-lego-client-secret",
      "oauth-microsoft-client-id",
      "oauth-microsoft-personal-client-id",
      "oauth-microsoft-personal-client-secret",
      "oauth-microsoft-work-client-id",
      "oauth-microsoft-work-client-secret",
      "oauth-paypal-client-id",
      "oauth-paypal-client-secret",
      "oauth-reddit-client-id",
      "oauth-reddit-client-secret",
      "oauth-roblox-client-id",
      "oauth-roblox-client-secret",
      "oauth-slack-client-id",
      "oauth-slack-client-secret",
      "oauth-spotify-client-id",
      "oauth-spotify-client-secret",
      "oauth-steam-api-key",
      "oauth-twitch-client-id",
      "oauth-twitch-client-secret",
      "oauth-xbox-client-id",
      "oauth-xbox-client-secret",
      "oauth-yahoo-client-id",
      "oauth-yahoo-client-secret"
    ],
    "BetterAuth": [
      "betterauth-secret",
      "betterauth-exchange-private-key",
      "better-auth-webhook-secret"
    ],
    "Infrastructure": [
      "postgres-password",
      "rabbitmq-password",
      "redis-password"
    ]
  }
}
```

**Important**: Secrets not in this allowlist cannot be retrieved, even with `secrets:*` permission. This provides defense-in-depth against misconfigured permissions.

#### `Readiness` Section

| Key | Description | Default |
|-----|-------------|---------|
| `ProbeSecretName` | Specific secret to probe during health checks | First allowed secret |
| `CheckCertificates` | Whether to include certificate access in health checks | `true` |

### Environment Variables

| Variable | Description |
|----------|-------------|
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID (fallback for `Secrets:AzureSubscriptionId`) |
| `AZURE_TENANT_ID` | Azure tenant ID (used by DefaultAzureCredential) |
| `AZURE_CLIENT_ID` | Azure client ID (used by DefaultAzureCredential) |
| `AZURE_CLIENT_SECRET` | Azure client secret (used by DefaultAzureCredential) |

---

## Architecture

### Component Diagram

```
                                    +------------------+
                                    |   Azure AD       |
                                    | (Token Issuer)   |
                                    +--------+---------+
                                             |
                                             | WIF Token
                                             v
+-------------+     JWT Token     +------------------+
|   Gateway   | ----------------> |  Dhadgar.Secrets |
|   (YARP)    |                   |                  |
+-------------+                   | +-------------+  |
                                  | | Authorization|  |
                                  | |   Service   |  |
                                  | +------+------+  |
                                  |        |         |
                                  | +------v------+  |
                                  | |   Secret    |  |     +------------------+
                                  | |  Provider   +------->|  Azure Key Vault |
                                  | |  (ISecretP  |  |     +------------------+
                                  | +-------------+  |
                                  |        |         |
                                  | +------v------+  |
                                  | | Audit Logger|  |
                                  | +-------------+  |
                                  +------------------+
```

### Directory Structure

```
src/Dhadgar.Secrets/
├── Program.cs                          # Application entry point and DI configuration
├── Hello.cs                            # Smoke test surface
├── appsettings.json                    # Base configuration
├── Properties/
│   └── launchSettings.json             # Development launch profiles
├── Authorization/
│   ├── ISecretsAuthorizationService.cs # Authorization interface and types
│   └── SecretsAuthorizationService.cs  # Permission checking implementation
├── Audit/
│   ├── ISecretsAuditLogger.cs          # Audit logging interface and event types
│   └── SecretsAuditLogger.cs           # Structured audit logging implementation
├── Endpoints/
│   ├── SecretsEndpoints.cs             # Read operations (GET, batch)
│   ├── SecretWriteEndpoints.cs         # Write operations (PUT, DELETE, rotate)
│   ├── CertificateEndpoints.cs         # Certificate management
│   └── KeyVaultEndpoints.cs            # Key Vault CRUD operations
├── Infrastructure/
│   └── UrlRewritingDocumentRetriever.cs # OIDC metadata URL rewriting for Docker/K8s
├── Options/
│   ├── SecretsOptions.cs               # Main configuration options
│   └── SecretsReadinessOptions.cs      # Health check configuration
├── Readiness/
│   └── SecretsReadinessCheck.cs        # Kubernetes readiness probe
├── Services/
│   ├── ISecretProvider.cs              # Secret provider interface
│   ├── KeyVaultSecretProvider.cs       # Azure Key Vault implementation
│   ├── DevelopmentSecretProvider.cs    # In-memory development implementation
│   ├── ICertificateProvider.cs         # Certificate provider interface
│   ├── KeyVaultCertificateProvider.cs  # Key Vault certificate operations
│   ├── IKeyVaultManager.cs             # Key Vault management interface
│   ├── AzureKeyVaultManager.cs         # ARM-based vault CRUD operations
│   └── WifCredentialProvider.cs        # Workload Identity Federation credentials
└── Validation/
    └── SecretNameValidator.cs          # Input validation for secret names
```

### Key Abstractions

#### `ISecretProvider`

The primary abstraction for secret storage operations:

```csharp
public interface ISecretProvider
{
    // Read operations
    Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default);
    bool IsAllowed(string secretName);

    // Write operations
    Task<bool> SetSecretAsync(string secretName, string value, CancellationToken ct = default);
    Task<(string Version, DateTime CreatedAt)> RotateSecretAsync(string secretName, CancellationToken ct = default);
    Task<bool> DeleteSecretAsync(string secretName, CancellationToken ct = default);
}
```

Two implementations:
- **`KeyVaultSecretProvider`**: Production implementation using Azure Key Vault
- **`DevelopmentSecretProvider`**: In-memory implementation for local development

#### `ISecretsAuthorizationService`

Handles permission checking with support for:
- Permission hierarchy (wildcards, categories, specific secrets)
- Break-glass access detection
- Service account vs user differentiation

```csharp
public interface ISecretsAuthorizationService
{
    AuthorizationResult Authorize(ClaimsPrincipal user, string secretName, SecretAction action);
    AuthorizationResult AuthorizeCategory(ClaimsPrincipal user, string category, SecretAction action);
}
```

#### `ISecretsAuditLogger`

Comprehensive audit logging for all secret operations:

```csharp
public interface ISecretsAuditLogger
{
    void LogAccess(SecretAuditEvent evt);
    void LogAccessDenied(SecretAccessDeniedEvent evt);
    void LogModification(SecretModificationEvent evt);
    void LogRotation(SecretRotationEvent evt);
    void LogBatchAccess(SecretBatchAccessEvent evt);
}
```

### Request Flow

1. **Request arrives** at the Gateway, which forwards to the Secrets service
2. **JWT validation** occurs via ASP.NET Core authentication middleware
3. **Rate limiting** checks are performed (read/write/rotate policies)
4. **Authorization** is checked via `ISecretsAuthorizationService`
5. **Allowlist check** ensures the secret is in the allowed set
6. **Secret retrieval** from the provider (Key Vault or development)
7. **Audit logging** records the access attempt and result
8. **Response** returned to the caller

---

## Authorization System

### Permission Format

Permissions follow the format: `secrets:{action}:{target}`

Where:
- **action**: `read`, `write`, `rotate`, `delete`, `list`, or `*` (wildcard)
- **target**: category name, specific secret name, or `*` (wildcard)

### Permission Hierarchy (Most to Least Specific)

The authorization service checks permissions in order from most specific to least specific:

1. `secrets:*` - Full admin access (all actions on all secrets)
2. `secrets:{action}:*` - Action wildcard (e.g., `secrets:read:*` for all reads)
3. `secrets:{action}:{category}` - Category permission (e.g., `secrets:read:oauth`)
4. `secrets:{action}:{secretName}` - Specific secret (e.g., `secrets:read:oauth-discord-client-id`)

### Permission Examples

| Permission | Grants |
|------------|--------|
| `secrets:*` | Full access to all secrets (admin) |
| `secrets:read:*` | Read any secret |
| `secrets:write:*` | Write/update any secret |
| `secrets:rotate:*` | Rotate any secret |
| `secrets:delete:*` | Delete any secret |
| `secrets:read:oauth` | Read all OAuth category secrets |
| `secrets:read:betterauth` | Read all BetterAuth category secrets |
| `secrets:read:infrastructure` | Read all infrastructure secrets |
| `secrets:read:oauth-discord-client-id` | Read only the Discord client ID |
| `secrets:write:oauth-discord-client-secret` | Write only the Discord client secret |

### Secret Categories

Categories are determined in two ways:

1. **Explicit membership** in `AllowedSecrets.OAuth`, `AllowedSecrets.BetterAuth`, or `AllowedSecrets.Infrastructure`
2. **Naming convention inference**:
   - Secrets starting with `oauth-` are categorized as `oauth`
   - Secrets starting with `betterauth-` are categorized as `betterauth`
   - Unknown patterns default to `custom` category

### JWT Claims Structure

The service expects these claims in the JWT token:

| Claim | Type | Description |
|-------|------|-------------|
| `sub` | string | User/service ID (required) |
| `principal_type` | string | `user` or `service` (defaults to `user`) |
| `permission` | string[] | Array of permission strings |
| `break_glass` | string | `true` if break-glass access is active |
| `break_glass_reason` | string | Reason for break-glass access |

Example JWT payload:
```json
{
  "sub": "svc-betterauth",
  "principal_type": "service",
  "permission": [
    "secrets:read:oauth",
    "secrets:read:betterauth"
  ],
  "iat": 1704067200,
  "exp": 1704070800,
  "iss": "https://meridianconsole.com/api/v1/identity",
  "aud": "meridian-api"
}
```

### Certificate Permissions

Certificate operations use a separate permission scheme:

| Permission | Grants |
|------------|--------|
| `secrets:read:certificates` | List certificates |
| `secrets:write:certificates` | Import and delete certificates |

### Key Vault Management Permissions

Key Vault CRUD operations use:

| Permission | Grants |
|------------|--------|
| `keyvault:read` | List and get vault details |
| `keyvault:write` | Create, update, and delete vaults |

---

## Audit Logging

### Log Event Types

The Secrets service logs all access attempts with structured fields optimized for SIEM integration.

#### `AUDIT:SECRETS:ACCESS` - Successful Secret Access

```
AUDIT:SECRETS:ACCESS Action=read Secret=oauth-discord-client-id User=svc-betterauth PrincipalType=service Success=True IsServiceAccount=True CorrelationId=abc123
```

Fields:
- `Action`: The operation performed (read, write, delete)
- `Secret`: Name of the accessed secret
- `User`: Subject ID from JWT
- `PrincipalType`: `user` or `service`
- `Success`: Whether the operation succeeded
- `IsServiceAccount`: Boolean indicating service account
- `CorrelationId`: Request trace ID

#### `AUDIT:SECRETS:DENIED` - Access Denied

```
AUDIT:SECRETS:DENIED Action=read Secret=infra-db-password User=user-123 Reason=Missing permission for Read on secret 'infra-db-password' CorrelationId=abc123
```

Logged at **Warning** level.

Fields:
- `Reason`: Human-readable denial reason

#### `AUDIT:SECRETS:BREAKGLASS` - Break-Glass Access

```
AUDIT:SECRETS:BREAKGLASS Action=read Secret=sensitive-key User=admin-1 PrincipalType=user Success=True CorrelationId=abc123
```

Logged at **Warning** level (always, even for successful access).

#### `AUDIT:SECRETS:MODIFY` - Secret Modification

```
AUDIT:SECRETS:MODIFY Action=write Secret=oauth-new-key User=admin-1 PrincipalType=user Success=True CorrelationId=abc123
```

For failures:
```
AUDIT:SECRETS:MODIFY:FAILED Action=write Secret=oauth-new-key User=admin-1 PrincipalType=user Error=Secret value exceeds 25600 byte limit CorrelationId=abc123
```

#### `AUDIT:SECRETS:ROTATED` - Secret Rotation

```
AUDIT:SECRETS:ROTATED Secret=oauth-discord-client-secret User=admin-1 PrincipalType=user NewVersion=a1b2c3d4 CorrelationId=abc123
```

Logged at **Warning** level (rotation is a significant security event).

For failures:
```
AUDIT:SECRETS:ROTATION:FAILED Secret=oauth-discord-client-secret User=admin-1 PrincipalType=user Error=Secret not in allowed list CorrelationId=abc123
```

#### `AUDIT:SECRETS:BATCH` - Batch Access

```
AUDIT:SECRETS:BATCH RequestedCount=5 AccessedCount=3 DeniedCount=2 User=svc-app CorrelationId=abc123
```

### Log Levels by Event Type

| Event Type | Log Level | Rationale |
|------------|-----------|-----------|
| Successful access | Information | Normal operation |
| Access denied | Warning | Potential unauthorized access attempt |
| Break-glass access | Warning | Emergency access requires attention |
| Modification success | Information | Normal operation |
| Modification failure | Error | Operational issue |
| Rotation success | Warning | Security-significant event |
| Rotation failure | Error | Operational issue |
| Batch access | Information | Normal operation |

### SIEM Integration

The structured log format is designed for easy parsing by SIEM systems:

1. **Prefix-based filtering**: All audit logs start with `AUDIT:SECRETS:`
2. **Consistent field names**: Fields use PascalCase for easy extraction
3. **CorrelationId**: Enables tracing related events across services
4. **Retention recommendation**: 90 days minimum (configure in log aggregation)

Example Loki/Grafana query:
```logql
{app="dhadgar-secrets"} |= "AUDIT:SECRETS:DENIED"
```

Example Splunk query:
```spl
index=meridian sourcetype=dhadgar-secrets "AUDIT:SECRETS:DENIED" | stats count by User
```

---

## Rate Limiting

### Rate Limit Policies

The service implements three fixed-window rate limit policies:

#### `SecretsRead` - Read Operations

```csharp
limiterOptions.PermitLimit = 100;      // 100 requests
limiterOptions.Window = TimeSpan.FromMinutes(1);  // per minute
limiterOptions.QueueLimit = 10;         // 10 queued requests max
```

Applies to:
- `GET /api/v1/secrets/{secretName}`
- `POST /api/v1/secrets/batch`
- `GET /api/v1/secrets/oauth`
- `GET /api/v1/secrets/betterauth`
- `GET /api/v1/secrets/infrastructure`

#### `SecretsWrite` - Write Operations

```csharp
limiterOptions.PermitLimit = 20;       // 20 requests
limiterOptions.Window = TimeSpan.FromMinutes(1);  // per minute
limiterOptions.QueueLimit = 5;          // 5 queued requests max
```

Applies to:
- `PUT /api/v1/secrets/{secretName}`
- `DELETE /api/v1/secrets/{secretName}`

#### `SecretsRotate` - Rotation Operations

```csharp
limiterOptions.PermitLimit = 5;        // 5 requests
limiterOptions.Window = TimeSpan.FromMinutes(1);  // per minute
limiterOptions.QueueLimit = 2;          // 2 queued requests max
```

Applies to:
- `POST /api/v1/secrets/{secretName}/rotate`

### Rate Limit Response

When rate limited, the service returns:

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 42

{
  "error": "Too many requests. Please try again later.",
  "retryAfterSeconds": 42
}
```

The `Retry-After` header and `retryAfterSeconds` field indicate when the client can retry.

---

## API Endpoints

### Standard Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Service banner |
| `/hello` | GET | Hello world message |
| `/healthz` | GET | Health check (live) |
| `/readyz` | GET | Readiness check |

### Secrets Endpoints

#### Get Single Secret

```http
GET /api/v1/secrets/{secretName}
Authorization: Bearer {jwt_token}
```

**Response (200 OK)**:
```json
{
  "name": "oauth-discord-client-id",
  "value": "1234567890"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid secret name format
- `403 Forbidden`: Secret not in allowlist or insufficient permissions
- `404 Not Found`: Secret not found in Key Vault

#### Get Multiple Secrets (Batch)

```http
POST /api/v1/secrets/batch
Authorization: Bearer {jwt_token}
Content-Type: application/json

{
  "secretNames": [
    "oauth-discord-client-id",
    "oauth-discord-client-secret",
    "oauth-github-client-id"
  ]
}
```

**Response (200 OK)**:
```json
{
  "secrets": {
    "oauth-discord-client-id": "1234567890",
    "oauth-discord-client-secret": "secret-value",
    "oauth-github-client-id": "gh-client-id"
  }
}
```

Note: Only secrets the caller is authorized to access will be returned. Denied secrets are silently omitted.

#### Get OAuth Secrets

```http
GET /api/v1/secrets/oauth
Authorization: Bearer {jwt_token}
```

Requires: `secrets:read:oauth` or higher permission.

**Response (200 OK)**:
```json
{
  "secrets": {
    "oauth-discord-client-id": "...",
    "oauth-discord-client-secret": "...",
    "oauth-github-client-id": "...",
    "oauth-github-client-secret": "..."
  }
}
```

#### Get BetterAuth Secrets

```http
GET /api/v1/secrets/betterauth
Authorization: Bearer {jwt_token}
```

Requires: `secrets:read:betterauth` or higher permission.

#### Get Infrastructure Secrets

```http
GET /api/v1/secrets/infrastructure
Authorization: Bearer {jwt_token}
```

Requires: `secrets:read:infrastructure` or higher permission.

#### Set/Update Secret

```http
PUT /api/v1/secrets/{secretName}
Authorization: Bearer {jwt_token}
Content-Type: application/json

{
  "value": "new-secret-value"
}
```

**Response (200 OK)**:
```json
{
  "name": "oauth-discord-client-secret",
  "updated": true
}
```

**Constraints**:
- Maximum value size: 25 KB (Azure Key Vault limit)

#### Rotate Secret

```http
POST /api/v1/secrets/{secretName}/rotate
Authorization: Bearer {jwt_token}
```

Generates a new cryptographically secure random value (32 bytes, base64 encoded).

**Response (200 OK)**:
```json
{
  "name": "oauth-discord-client-secret",
  "version": "a1b2c3d4e5f6",
  "rotatedAt": "2024-01-01T12:00:00Z",
  "expiresAt": null
}
```

#### Delete Secret

```http
DELETE /api/v1/secrets/{secretName}
Authorization: Bearer {jwt_token}
```

**Response (204 No Content)**: Success

**Response (404 Not Found)**: Secret not found

Note: If soft delete is enabled on the Key Vault, the secret enters a recoverable state.

### Certificate Endpoints

#### List Certificates

```http
GET /api/v1/certificates
Authorization: Bearer {jwt_token}
```

Requires: `secrets:read:certificates` permission.

**Response (200 OK)**:
```json
{
  "certificates": [
    {
      "name": "api-signing-cert",
      "subject": "CN=api.meridianconsole.com",
      "issuer": "Let's Encrypt",
      "expiresAt": "2024-12-31T23:59:59Z",
      "thumbprint": "ABC123...",
      "enabled": true
    }
  ]
}
```

#### List Certificates in Specific Vault

```http
GET /api/v1/keyvaults/{vaultName}/certificates
Authorization: Bearer {jwt_token}
```

#### Import Certificate

```http
POST /api/v1/certificates
Authorization: Bearer {jwt_token}
Content-Type: application/json

{
  "name": "my-certificate",
  "certificateData": "BASE64_ENCODED_PFX_DATA",
  "password": "optional-pfx-password"
}
```

**Response (200 OK)**:
```json
{
  "name": "my-certificate",
  "subject": "CN=example.com",
  "issuer": "My CA",
  "thumbprint": "ABC123...",
  "expiresAt": "2025-12-31T23:59:59Z"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid certificate format or wrong password
- `409 Conflict`: Certificate with this name already exists

#### Delete Certificate

```http
DELETE /api/v1/certificates/{name}
Authorization: Bearer {jwt_token}
```

### Key Vault Management Endpoints

#### List Key Vaults

```http
GET /api/v1/keyvaults
Authorization: Bearer {jwt_token}
```

Requires: `keyvault:read` permission.

**Response (200 OK)**:
```json
{
  "vaults": [
    {
      "name": "mc-oauth",
      "vaultUri": "https://mc-oauth.vault.azure.net/",
      "location": "centralus",
      "secretCount": 15,
      "enabled": true
    }
  ]
}
```

#### Get Key Vault Details

```http
GET /api/v1/keyvaults/{vaultName}
Authorization: Bearer {jwt_token}
```

**Response (200 OK)**:
```json
{
  "name": "mc-oauth",
  "vaultUri": "https://mc-oauth.vault.azure.net/",
  "location": "centralus",
  "resourceGroup": "meridian-rg",
  "sku": "Standard",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "enableSoftDelete": true,
  "enablePurgeProtection": true,
  "softDeleteRetentionDays": 90,
  "enableRbacAuthorization": true,
  "publicNetworkAccess": "Enabled",
  "secretCount": 15,
  "keyCount": 2,
  "certificateCount": 3,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-15T12:00:00Z"
}
```

#### Create Key Vault

```http
POST /api/v1/keyvaults
Authorization: Bearer {jwt_token}
Content-Type: application/json

{
  "name": "new-vault-name",
  "location": "eastus",
  "resourceGroupName": "my-resource-group"
}
```

Requires: `keyvault:write` permission.

**Constraints**:
- Name must be 3-24 characters
- Name can only contain letters, numbers, and hyphens

**Default Settings**:
- SKU: Standard
- Soft delete: Enabled (90 days)
- Purge protection: Enabled
- RBAC authorization: Enabled

#### Update Key Vault

```http
PATCH /api/v1/keyvaults/{vaultName}
Authorization: Bearer {jwt_token}
Content-Type: application/json

{
  "enableSoftDelete": true,
  "enablePurgeProtection": true,
  "softDeleteRetentionDays": 90,
  "sku": "Premium"
}
```

**Note**: Purge protection cannot be disabled once enabled.

#### Delete Key Vault

```http
DELETE /api/v1/keyvaults/{vaultName}
Authorization: Bearer {jwt_token}
```

Performs soft delete if enabled. Manual purge requires Azure CLI:
```bash
az keyvault purge --name {vaultName} --location {location}
```

---

## Break-Glass Access

### Purpose

Break-glass access provides emergency access to secrets when normal authorization would deny access. This is intended for:

- Critical production incidents
- Security emergency response
- System recovery scenarios

### How It Works

1. An administrator issues a JWT with `break_glass: true` claim
2. The JWT should include `break_glass_reason` explaining the emergency
3. The Secrets service grants access regardless of other permissions
4. All break-glass access is logged at **Warning** level
5. The break-glass reason is recorded in audit logs

### JWT Structure for Break-Glass

```json
{
  "sub": "emergency-admin",
  "principal_type": "user",
  "break_glass": "true",
  "break_glass_reason": "Production database connection string leaked, rotating all infra credentials",
  "iat": 1704067200,
  "exp": 1704070800,
  "iss": "https://meridianconsole.com/api/v1/identity",
  "aud": "meridian-api"
}
```

### Audit Log Entry

```
AUDIT:SECRETS:BREAKGLASS Action=read Secret=postgres-password User=emergency-admin PrincipalType=user Success=True CorrelationId=incident-2024-001
```

### Security Considerations

1. **Short-lived tokens**: Break-glass tokens should have minimal expiration (e.g., 15 minutes)
2. **Audit review**: All break-glass access should be reviewed post-incident
3. **Token issuance control**: Only trusted administrators should be able to issue break-glass tokens
4. **Reason requirement**: Always include a reason for audit trail completeness

---

## Service Accounts vs Users

### Differentiation

The Secrets service distinguishes between:

- **User principals** (`principal_type: user`): Human administrators or operators
- **Service principals** (`principal_type: service`): Automated services like BetterAuth, the Gateway, etc.

### Why This Matters

1. **Audit clarity**: Logs clearly show whether access was human or automated
2. **Permission scoping**: Services typically need narrower permissions
3. **Anomaly detection**: Unusual patterns from service accounts may indicate compromise

### JWT Structure for Service Accounts

```json
{
  "sub": "svc-betterauth",
  "principal_type": "service",
  "permission": [
    "secrets:read:oauth",
    "secrets:read:betterauth"
  ],
  "iat": 1704067200,
  "exp": 1704070800,
  "iss": "https://meridianconsole.com/api/v1/identity",
  "aud": "meridian-api"
}
```

### Audit Log Differences

Service account access:
```
AUDIT:SECRETS:ACCESS Action=read Secret=oauth-discord-client-id User=svc-betterauth PrincipalType=service Success=True IsServiceAccount=True CorrelationId=abc123
```

User access:
```
AUDIT:SECRETS:ACCESS Action=read Secret=oauth-discord-client-id User=admin-1 PrincipalType=user Success=True IsServiceAccount=False CorrelationId=abc123
```

### Default Behavior

If `principal_type` claim is missing, the service defaults to `user`.

---

## Azure Key Vault Integration

### Authentication Methods

The service supports multiple Azure authentication methods via the `WifCredentialProvider`:

#### 1. Workload Identity Federation (Recommended for Kubernetes)

When WIF is configured, the service:
1. Requests a JWT from the Identity service using client credentials
2. Uses that JWT as a client assertion to authenticate to Azure AD
3. Receives an Azure access token for Key Vault access

This eliminates the need to store Azure client secrets.

**Configuration**:
```json
{
  "Secrets": {
    "Wif": {
      "TenantId": "azure-tenant-id",
      "ClientId": "app-registration-client-id",
      "IdentityTokenEndpoint": "http://identity:8080/connect/token",
      "ServiceClientId": "secrets-service",
      "ServiceClientSecret": "service-secret"
    }
  }
}
```

#### 2. DefaultAzureCredential (Fallback)

When WIF is not configured, the service falls back to `DefaultAzureCredential`, which tries in order:
1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`)
2. Managed Identity (in Azure)
3. Azure CLI credentials (local development)
4. Visual Studio credentials
5. Azure PowerShell credentials

### Secret Provider Features

#### Caching

The `KeyVaultSecretProvider` implements a 5-minute memory cache to reduce Key Vault API calls:

```csharp
private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
```

Cache is invalidated automatically on:
- `SetSecretAsync`
- `RotateSecretAsync`
- `DeleteSecretAsync`

#### Placeholder Handling

Secrets with the value `PLACEHOLDER-UPDATE-ME` are treated as not configured and return `null`:

```csharp
if (value == "PLACEHOLDER-UPDATE-ME")
{
    return null;
}
```

#### Size Limits

Azure Key Vault has a 25 KB limit for secret values. The service enforces this on writes:

```csharp
const int maxSizeBytes = 25 * 1024;
var valueBytes = System.Text.Encoding.UTF8.GetByteCount(value);
if (valueBytes > maxSizeBytes)
{
    throw new InvalidOperationException($"Secret value exceeds {maxSizeBytes} byte limit");
}
```

### Certificate Provider

The `KeyVaultCertificateProvider` handles:
- Listing certificate properties and policies
- Importing PFX/PKCS12 certificates
- Deleting certificates (soft delete if enabled)

### Key Vault Manager

The `AzureKeyVaultManager` uses the Azure Resource Manager SDK to:
- List all Key Vaults in a subscription
- Get vault details including secret/key/certificate counts
- Create new vaults with secure defaults
- Update vault properties (SKU, retention, etc.)
- Delete vaults (soft delete)

**Default vault creation settings**:
- SKU: Standard
- Soft delete: Enabled
- Purge protection: Enabled
- Retention: 90 days
- RBAC authorization: Enabled
- Public network access: Enabled

---

## Development Provider

### Purpose

The `DevelopmentSecretProvider` provides an in-memory secret store for local development without requiring Azure Key Vault access.

### Configuration

Enable in `appsettings.Development.json`:

```json
{
  "Secrets": {
    "UseDevelopmentProvider": true,
    "Development": {
      "Secrets": {
        "oauth-discord-client-id": "dev-discord-client-id",
        "oauth-discord-client-secret": "dev-discord-secret",
        "oauth-github-client-id": "dev-github-client-id",
        "postgres-password": "dhadgar",
        "rabbitmq-password": "dhadgar",
        "redis-password": "dhadgar"
      }
    }
  }
}
```

### Behavior

- **Allowlist enforcement**: Same as Key Vault provider
- **Placeholder filtering**: Values of `PLACEHOLDER-UPDATE-ME` are excluded
- **In-memory storage**: Secrets are held in a dictionary
- **Write support**: `SetSecretAsync` updates the in-memory store
- **Rotation support**: Generates new random base64 values
- **No persistence**: Changes are lost on restart

### Health Check Behavior

When using the development provider, the readiness check:
1. Skips Key Vault connectivity tests
2. Reports `secrets_provider: development`
3. Returns healthy status

---

## Testing

### Test Project

Tests are located in `tests/Dhadgar.Secrets.Tests/`:

```
tests/Dhadgar.Secrets.Tests/
├── HelloWorldTests.cs                      # Smoke tests
├── ReadinessTests.cs                       # Health check tests
├── ReadinessIntegrationTests.cs            # Integration health tests
├── Authorization/
│   └── SecretsAuthorizationServiceTests.cs # Comprehensive auth tests
├── Validation/
│   └── SecretNameValidatorTests.cs         # Input validation tests
└── Security/
    └── SecretsSecurityIntegrationTests.cs  # Security integration tests
```

### Running Tests

```bash
# Run all Secrets service tests
dotnet test tests/Dhadgar.Secrets.Tests

# Run specific test class
dotnet test tests/Dhadgar.Secrets.Tests --filter "FullyQualifiedName~SecretsAuthorizationServiceTests"

# Run with verbose output
dotnet test tests/Dhadgar.Secrets.Tests --verbosity normal
```

### Test Coverage Areas

#### Authorization Tests (`SecretsAuthorizationServiceTests`)

- Unauthenticated access denial
- Full admin (`secrets:*`) permission
- Action wildcards (`secrets:read:*`, `secrets:write:*`)
- Category permissions (`secrets:read:oauth`)
- Specific secret permissions (`secrets:read:oauth-discord-client-id`)
- Break-glass access
- Service account detection
- Category inference from naming conventions
- Multiple permissions
- Case-insensitive permission matching
- Denial information completeness

#### Validation Tests (`SecretNameValidatorTests`)

- Valid names (single char, multi-char, max length)
- Empty/null name rejection
- Length limit enforcement (127 chars)
- Invalid character rejection
- Dash placement rules
- Injection pattern detection (path traversal, SQL injection, XSS)
- Unicode/emoji rejection

### Writing New Tests

```csharp
using Xunit;
using Dhadgar.Secrets.Authorization;

public class MyNewTests
{
    [Fact]
    public void MyFeature_GivenCondition_ShouldBehaveCorrectly()
    {
        // Arrange
        var service = CreateAuthorizationService();
        var user = CreateUser("user-1", "secrets:read:oauth");

        // Act
        var result = service.Authorize(user, "oauth-test", SecretAction.Read);

        // Assert
        Assert.True(result.IsAuthorized);
    }
}
```

---

## Security Considerations

### Defense in Depth

The service implements multiple security layers:

1. **Network layer**: Service runs behind Gateway/YARP
2. **Authentication**: JWT tokens validated against Identity service
3. **Authorization**: Permission-based access control
4. **Allowlist**: Only explicitly allowed secrets can be dispensed
5. **Input validation**: Secret names validated against injection patterns
6. **Rate limiting**: Prevents abuse and runaway service consumption
7. **Audit logging**: All access attempts recorded
8. **Caching**: Reduces exposure of Key Vault credentials

### Input Validation Rules

The `SecretNameValidator` enforces:

- **Length**: 1-127 characters
- **Characters**: Alphanumeric and dashes only
- **Format**: Must start and end with alphanumeric character
- **No injection patterns**: Blocks `..`, `/`, `\`, `'`, `;`, `<`, `>`, null bytes

### Secrets That Should NOT Be Dispensed

The Identity service maintains direct Key Vault access for:
- JWT signing keys
- OAuth state encryption keys
- Internal authentication secrets

These should NEVER be added to the Secrets service allowlist.

### Principle of Least Privilege

- Service accounts should have narrowly scoped permissions
- Use category permissions over wildcards when possible
- Use specific secret permissions for sensitive operations
- Avoid granting `secrets:*` except to true administrators

### Monitoring Recommendations

1. **Alert on break-glass access**: Any `AUDIT:SECRETS:BREAKGLASS` log entry
2. **Alert on high denial rates**: Sudden spike in `AUDIT:SECRETS:DENIED` events
3. **Alert on rotation failures**: `AUDIT:SECRETS:ROTATION:FAILED` events
4. **Monitor rate limit hits**: Track 429 responses for capacity planning
5. **Track service account patterns**: Unusual access patterns may indicate compromise

### Credential Rotation

For production deployments:
1. Rotate Azure service principal credentials regularly
2. Use Workload Identity Federation to eliminate stored credentials
3. Rotate secrets using the `/rotate` endpoint
4. Monitor Key Vault audit logs for unauthorized access

---

## Related Documentation

### Project Documentation

- [Main CLAUDE.md](/CLAUDE.md) - Overall project guidance
- [Docker Compose README](/deploy/compose/README.md) - Local infrastructure setup
- [Kubernetes Deployment](/deploy/kubernetes/) - Production deployment (planned)

### Azure Documentation

- [Azure Key Vault Documentation](https://docs.microsoft.com/en-us/azure/key-vault/)
- [Workload Identity Federation](https://docs.microsoft.com/en-us/azure/active-directory/workload-identities/)
- [Key Vault Best Practices](https://docs.microsoft.com/en-us/azure/key-vault/general/best-practices)

### Related Services

- **Dhadgar.Identity** - Issues JWT tokens with permissions claims
- **Dhadgar.Gateway** - Routes requests to Secrets service
- **Dhadgar.ServiceDefaults** - Provides correlation middleware

### OpenTelemetry

- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [OTLP Exporter](https://opentelemetry.io/docs/collector/)

---

## Appendix: Default Allowed Secrets

### OAuth Provider Secrets

| Secret Name | Provider |
|-------------|----------|
| `oauth-amazon-client-id` | Amazon (Login with Amazon) |
| `oauth-amazon-client-secret` | Amazon |
| `oauth-battlenet-client-id` | Battle.net (Blizzard) |
| `oauth-battlenet-client-secret` | Battle.net |
| `oauth-discord-client-id` | Discord |
| `oauth-discord-client-secret` | Discord |
| `oauth-facebook-app-id` | Facebook |
| `oauth-facebook-app-secret` | Facebook |
| `oauth-github-client-id` | GitHub |
| `oauth-github-client-secret` | GitHub |
| `oauth-google-client-id` | Google |
| `oauth-google-client-secret` | Google |
| `oauth-lego-client-id` | LEGO ID |
| `oauth-lego-client-secret` | LEGO ID |
| `oauth-microsoft-client-id` | Microsoft (unified) |
| `oauth-microsoft-personal-client-id` | Microsoft (personal) |
| `oauth-microsoft-personal-client-secret` | Microsoft (personal) |
| `oauth-microsoft-work-client-id` | Microsoft (work) |
| `oauth-microsoft-work-client-secret` | Microsoft (work) |
| `oauth-paypal-client-id` | PayPal |
| `oauth-paypal-client-secret` | PayPal |
| `oauth-reddit-client-id` | Reddit |
| `oauth-reddit-client-secret` | Reddit |
| `oauth-roblox-client-id` | Roblox |
| `oauth-roblox-client-secret` | Roblox |
| `oauth-slack-client-id` | Slack |
| `oauth-slack-client-secret` | Slack |
| `oauth-spotify-client-id` | Spotify |
| `oauth-spotify-client-secret` | Spotify |
| `oauth-steam-api-key` | Steam (OpenID 2.0) |
| `oauth-twitch-client-id` | Twitch |
| `oauth-twitch-client-secret` | Twitch |
| `oauth-xbox-client-id` | Xbox |
| `oauth-xbox-client-secret` | Xbox |
| `oauth-yahoo-client-id` | Yahoo |
| `oauth-yahoo-client-secret` | Yahoo |

### BetterAuth Secrets

| Secret Name | Purpose |
|-------------|---------|
| `betterauth-secret` | Main BetterAuth secret |
| `betterauth-exchange-private-key` | Token exchange key |
| `better-auth-webhook-secret` | Webhook verification |

### Infrastructure Secrets

| Secret Name | Purpose |
|-------------|---------|
| `postgres-password` | PostgreSQL password |
| `rabbitmq-password` | RabbitMQ password |
| `redis-password` | Redis password |
