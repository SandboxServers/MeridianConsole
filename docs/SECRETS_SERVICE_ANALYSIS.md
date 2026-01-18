# Secrets Service Analysis: Path to Production

**Document Version**: 1.0
**Date**: 2026-01-16
**Status**: Analysis Complete - Ready for Implementation Planning

---

## Executive Summary

The Secrets service provides centralized management of OAuth credentials, infrastructure secrets, and certificates for the Meridian Console platform. The current implementation is **well-architected** with solid foundations: Azure Key Vault integration, JWT authentication, category-based permissions, CLI tooling, and comprehensive endpoint coverage.

However, **critical gaps** exist that must be addressed before production deployment:

| Gap | Severity | Impact |
|-----|----------|--------|
| **No tenant isolation** | CRITICAL | Cross-tenant data leakage possible |
| **Missing audit logging** | HIGH | No forensic trail for secret access |
| **No service account distinction** | HIGH | Cannot differentiate service vs user access |
| **Missing delegation support** | MEDIUM | Services cannot act on behalf of users |
| **Permission hierarchy gaps** | MEDIUM | No wildcard or implied permissions |

**Estimated effort**: 3 phases over multiple sprints to reach production-ready hardened status.

---

## Table of Contents

1. [Current State Analysis](#1-current-state-analysis)
2. [Target State Definition](#2-target-state-definition)
3. [Gap Analysis](#3-gap-analysis)
4. [Implementation Roadmap](#4-implementation-roadmap)
5. [Endpoint Specifications](#5-endpoint-specifications)
6. [Service Layer Changes](#6-service-layer-changes)
7. [CLI Updates](#7-cli-updates)
8. [Testing Strategy](#8-testing-strategy)
9. [Documentation Updates](#9-documentation-updates)
10. [Security Checklist](#10-security-checklist)

---

## 1. Current State Analysis

### 1.1 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                           Gateway (YARP)                            │
│                    TenantScoped + RateLimiter                       │
└─────────────────────────────┬───────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Secrets Service                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │ SecretsEndpoints│  │SecretWrite      │  │ CertificateEndpoints│  │
│  │ (GET, POST batch)│  │Endpoints        │  │ (CRUD)              │  │
│  └────────┬────────┘  └────────┬────────┘  └──────────┬──────────┘  │
│           │                    │                       │             │
│           └────────────────────┼───────────────────────┘             │
│                                ▼                                     │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │                     ISecretProvider                              │ │
│  │    ┌─────────────────────┐  ┌─────────────────────────────┐    │ │
│  │    │DevelopmentSecret    │  │KeyVaultSecretProvider       │    │ │
│  │    │Provider (in-memory) │  │(Azure Key Vault + cache)    │    │ │
│  │    └─────────────────────┘  └─────────────────────────────┘    │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Azure Key Vault                                   │
│         (Secrets, Certificates, Keys - RBAC enabled)                │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 Current Endpoint Inventory

| Endpoint | Method | Purpose | Auth Required |
|----------|--------|---------|---------------|
| `/api/v1/secrets/{secretName}` | GET | Get single secret | Yes |
| `/api/v1/secrets/{secretName}` | PUT | Set/update secret | Yes |
| `/api/v1/secrets/{secretName}` | DELETE | Delete secret | Yes |
| `/api/v1/secrets/{secretName}/rotate` | POST | Rotate secret | Yes |
| `/api/v1/secrets/batch` | POST | Get multiple secrets | Yes |
| `/api/v1/secrets/oauth` | GET | Get all OAuth secrets | Yes |
| `/api/v1/secrets/betterauth` | GET | Get BetterAuth secrets | Yes |
| `/api/v1/secrets/infrastructure` | GET | Get infrastructure secrets | Yes |
| `/api/v1/certificates` | GET | List certificates | Yes |
| `/api/v1/certificates` | POST | Import certificate | Yes |
| `/api/v1/certificates/{name}` | DELETE | Delete certificate | Yes |
| `/api/v1/keyvaults` | GET | List Key Vaults | Yes |
| `/api/v1/keyvaults` | POST | Create Key Vault | Yes |
| `/api/v1/keyvaults/{vaultName}` | GET/PATCH/DELETE | Manage vault | Yes |

### 1.3 Current Permission Model

```
secrets:read:{secretName}     # Direct secret access
secrets:write:{secretName}    # Write specific secret
secrets:rotate:{secretName}   # Rotate specific secret (stricter than write)

secrets:read:oauth            # Read all OAuth secrets
secrets:read:betterauth       # Read BetterAuth secrets
secrets:read:infrastructure   # Read infrastructure secrets

secrets:read:certificates     # Certificate read operations
secrets:write:certificates    # Certificate write operations

keyvault:read                 # List/view Key Vaults
keyvault:write                # Create/modify Key Vaults
```

### 1.4 Current Files Structure

```
src/Dhadgar.Secrets/
├── Program.cs                           # Service entry, DI, middleware
├── appsettings.json                     # Configuration
├── Dhadgar.Secrets.csproj               # Dependencies
├── Endpoints/
│   ├── SecretsEndpoints.cs              # Read operations
│   ├── SecretWriteEndpoints.cs          # Write operations
│   ├── CertificateEndpoints.cs          # Certificate management
│   └── KeyVaultEndpoints.cs             # Key Vault management
├── Options/
│   ├── SecretsOptions.cs                # Configuration options
│   └── SecretsReadinessOptions.cs       # Health check options
├── Readiness/
│   └── SecretsReadinessCheck.cs         # Health check implementation
└── Services/
    ├── ISecretProvider.cs               # Interface (inline in KeyVault file)
    ├── KeyVaultSecretProvider.cs        # Production provider
    ├── DevelopmentSecretProvider.cs     # Development provider
    ├── ICertificateProvider.cs          # Certificate interface
    ├── KeyVaultCertificateProvider.cs   # Certificate implementation
    ├── IKeyVaultManager.cs              # Vault management interface
    └── AzureKeyVaultManager.cs          # Vault management implementation
```

### 1.5 Identity Integration Status

**What exists today:**
- JWT Bearer authentication configured
- Permission claims read from tokens
- Issuer/audience validation against Identity service

**What's missing:**
- `org_id` claim not validated against secret scope
- No `principal_type` claim to distinguish users vs services
- No delegation/on-behalf-of support
- Secrets permissions not defined in Identity's `RoleDefinitions.cs`

---

## 2. Target State Definition

### 2.1 Security Requirements

| Requirement | Description |
|-------------|-------------|
| **Tenant Isolation** | Secrets must be scoped to organizations; no cross-tenant access |
| **Audit Trail** | All secret access logged with user, org, IP, timestamp, action |
| **Service Identity** | Service accounts distinguishable from users with appropriate permissions |
| **Delegation** | Services can act on behalf of users with reduced scope |
| **Break-Glass** | Emergency access mechanism with mandatory audit |
| **Rate Limiting** | Per-user/tenant throttling to prevent abuse |
| **Input Validation** | All inputs sanitized against injection attacks |

### 2.2 Target Permission Model

```
# Hierarchical format: {resource}:{action}:{scope}

# Platform-level (no tenant prefix)
secrets:*                              # Full admin (platform only)
secrets:read:*                         # Read all categories
secrets:write:*                        # Write all categories
secrets:rotate:*                       # Rotate all secrets

# Tenant-scoped (with org prefix)
secrets:read:oauth:tenant              # Read OAuth for own tenant
secrets:write:oauth:tenant             # Write OAuth for own tenant
secrets:read:infrastructure:tenant     # Read infrastructure for own tenant

# Specific secret access
secrets:read:discord-client-id         # Single secret
secrets:write:discord-client-id        # Single secret write

# Certificate permissions
certificates:read                      # List/view certificates
certificates:write                     # Import/delete certificates

# Key Vault management (platform admin only)
keyvault:read                          # View vaults
keyvault:write                         # Create/modify vaults
keyvault:delete                        # Delete vaults

# Break-glass
break-glass:secrets                    # Emergency full access
```

### 2.3 Target Role Definitions

Add to `src/Dhadgar.Identity/Authorization/RoleDefinitions.cs`:

```csharp
// Existing system roles - add secrets permissions
["owner"] = new RoleDefinition
{
    // ... existing claims ...
    ImpliedClaims = existing.Concat(new[]
    {
        "secrets:read:oauth:tenant",
        "secrets:write:oauth:tenant",
        "secrets:read:infrastructure:tenant",
        "certificates:read"
    }).ToArray()
},

["admin"] = new RoleDefinition
{
    // ... existing claims ...
    ImpliedClaims = existing.Concat(new[]
    {
        "secrets:read:oauth:tenant",
        "secrets:read:infrastructure:tenant"
    }).ToArray()
},

// New secrets-specific roles
["secrets-admin"] = new RoleDefinition
{
    Name = "Secrets Administrator",
    Description = "Full control over secrets within tenant",
    ImpliedClaims = new[]
    {
        "secrets:*:tenant",
        "certificates:*"
    }
},

["secrets-rotator"] = new RoleDefinition
{
    Name = "Secrets Rotator",
    Description = "Can rotate secrets but not read values",
    ImpliedClaims = new[]
    {
        "secrets:rotate:*:tenant",
        "secrets:list:*"
    }
}
```

### 2.4 Secret Naming Convention

```
# Platform secrets (no prefix)
betterauth-secret
postgres-password
rabbitmq-password

# Tenant secrets (with org prefix)
{org-slug}/oauth/discord-client-id
{org-slug}/oauth/discord-client-secret
{org-slug}/integration/webhook-secret
```

### 2.5 Target Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                           Gateway (YARP)                            │
│              TenantScoped + SecretsRateLimiter + CircuitBreaker     │
└─────────────────────────────┬───────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Secrets Service                               │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │              Authorization Middleware                            │ │
│  │   - Tenant isolation enforcement                                 │ │
│  │   - Permission hierarchy resolution                              │ │
│  │   - Service vs User principal detection                          │ │
│  │   - Delegation validation                                        │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                │                                     │
│  ┌─────────────────────────────┼───────────────────────────────────┐ │
│  │              Security Audit Logger                              │ │
│  │   - All access logged with context                               │ │
│  │   - Break-glass access flagged                                   │ │
│  │   - Denial reasons captured                                      │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                │                                     │
│  ┌─────────────────────────────┼───────────────────────────────────┐ │
│  │                    Endpoints                                     │ │
│  │   SecretsEndpoints | SecretWriteEndpoints | CertificateEndpoints │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                │                                     │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │              ISecretProvider (with tenant context)              │ │
│  │   - Secret name prefixing                                        │ │
│  │   - Cross-tenant protection                                      │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 3. Gap Analysis

### 3.1 Critical Gaps (P0 - Block Production)

| Gap | Current State | Required State | Impact if Not Fixed |
|-----|---------------|----------------|---------------------|
| **Tenant isolation** | `org_id` not validated | Validate org_id matches secret scope | Cross-tenant data leak |
| **Audit logging** | Debug-level logs only | Security event logging with context | No forensic capability |
| **Input validation** | No secret name validation | Regex validation, injection prevention | Security vulnerability |

### 3.2 High Priority Gaps (P1)

| Gap | Current State | Required State | Impact if Not Fixed |
|-----|---------------|----------------|---------------------|
| **Service accounts** | Not distinguished | `principal_type` claim | Cannot audit service vs user |
| **Rate limiting** | Gateway only | Service-level limits | Enumeration attacks possible |
| **Permission hierarchy** | Flat permissions | Hierarchical with wildcards | Management overhead |
| **Error masking** | Azure errors exposed | Generic error responses | Information disclosure |

### 3.3 Medium Priority Gaps (P2)

| Gap | Current State | Required State | Impact if Not Fixed |
|-----|---------------|----------------|---------------------|
| **Delegation** | Not implemented | RFC 8693 token exchange | Services cannot preserve user context |
| **Break-glass** | Not implemented | Audited emergency access | Operational incidents harder |
| **Token lifetime** | Fixed 15 min | Category-based lifetime | Over-exposed for sensitive ops |
| **Cache security** | 5-min cache | Reduced + encrypted | Extended exposure window |

### 3.4 Files Requiring Changes

| File | Change Type | Priority |
|------|-------------|----------|
| `src/Dhadgar.Secrets/Endpoints/SecretsEndpoints.cs` | Major refactor | P0 |
| `src/Dhadgar.Secrets/Endpoints/SecretWriteEndpoints.cs` | Major refactor | P0 |
| `src/Dhadgar.Secrets/Services/KeyVaultSecretProvider.cs` | Add tenant context | P0 |
| `src/Dhadgar.Secrets/Program.cs` | Add middleware, rate limiting | P1 |
| `src/Dhadgar.Identity/Authorization/RoleDefinitions.cs` | Add secrets permissions | P1 |
| `src/Dhadgar.Identity/Services/PermissionService.cs` | Add service account handling | P1 |
| `src/Shared/Dhadgar.ServiceDefaults/Security/SecretsAuditLogger.cs` | New file | P0 |
| `src/Dhadgar.Secrets/Authorization/SecretsAuthorizationHandler.cs` | New file | P0 |
| `src/Dhadgar.Contracts/Secrets/*` | DTOs for new patterns | P1 |
| `src/Dhadgar.Cli/Commands/Secret/*.cs` | Add tenant context | P2 |

---

## 4. Implementation Roadmap

### Phase 1: Security Foundation (Critical)

**Goal**: Establish tenant isolation and audit trail - minimum viable security.

#### 4.1.1 Tenant Isolation

1. **Add `IOrganizationContext` to all endpoints**
   - Inject organization context from JWT claims
   - Validate `org_id` matches secret scope

2. **Implement secret naming with tenant prefix**
   - Platform secrets: No prefix (require platform admin)
   - Tenant secrets: `{org-slug}/{category}/{name}`

3. **Create `SecretsAuthorizationHandler`**
   - Centralize permission checking
   - Validate tenant membership
   - Support hierarchical permissions

#### 4.1.2 Audit Logging

1. **Create `ISecretsAuditLogger` interface**
   ```csharp
   public interface ISecretsAuditLogger
   {
       Task LogSecretAccessAsync(SecretAccessEvent evt, CancellationToken ct);
       Task LogSecretModificationAsync(SecretModificationEvent evt, CancellationToken ct);
       Task LogAuthorizationDeniedAsync(SecretDenialEvent evt, CancellationToken ct);
   }
   ```

2. **Implement structured audit events**
   - User/service ID
   - Organization ID
   - Secret name (never value!)
   - Action (read/write/rotate/delete)
   - Source IP
   - Correlation ID
   - Timestamp
   - Success/failure
   - Denial reason (if applicable)

3. **Wire into all endpoints**

#### 4.1.3 Input Validation

1. **Add `SecretNameValidator`**
   ```csharp
   public static class SecretNameValidator
   {
       private static readonly Regex ValidNamePattern =
           new(@"^[a-zA-Z0-9]([a-zA-Z0-9-/]*[a-zA-Z0-9])?$", RegexOptions.Compiled);

       public static bool IsValid(string name) =>
           !string.IsNullOrWhiteSpace(name) &&
           name.Length <= 127 &&
           ValidNamePattern.IsMatch(name) &&
           !name.Contains("..") &&
           !name.StartsWith("/") &&
           !name.EndsWith("/");
   }
   ```

2. **Add FluentValidation for DTOs**

### Phase 2: Identity Integration (High Priority)

**Goal**: Proper service accounts, role-based access, rate limiting.

#### 4.2.1 Service Account Support

1. **Add `principal_type` claim to Identity service**
   - `user` - Human user
   - `service` - Service account
   - `agent` - Customer-hosted agent

2. **Update token generation in Identity**
   ```csharp
   claims.Add(new Claim("principal_type",
       isServiceAccount ? "service" : "user"));
   ```

3. **Update Secrets service to detect and handle differently**

#### 4.2.2 Role Definitions

1. **Add secrets permissions to existing roles in `RoleDefinitions.cs`**
2. **Add new secrets-specific roles**
3. **Update permission service to resolve hierarchical permissions**

#### 4.2.3 Service-Level Rate Limiting

1. **Add rate limiting middleware to Secrets service**
   ```csharp
   builder.Services.AddRateLimiter(options =>
   {
       options.AddPolicy("SecretsRead", context =>
           RateLimitPartition.GetFixedWindowLimiter(
               partitionKey: GetPartitionKey(context),
               factory: _ => new FixedWindowRateLimiterOptions
               {
                   PermitLimit = 100,
                   Window = TimeSpan.FromMinutes(1)
               }));

       options.AddPolicy("SecretsWrite", context =>
           RateLimitPartition.GetFixedWindowLimiter(
               partitionKey: GetPartitionKey(context),
               factory: _ => new FixedWindowRateLimiterOptions
               {
                   PermitLimit = 20,
                   Window = TimeSpan.FromMinutes(1)
               }));
   });
   ```

### Phase 3: Advanced Features (Medium Priority)

**Goal**: Delegation, break-glass, enhanced security features.

#### 4.3.1 Delegation Support

1. **Implement RFC 8693 token exchange in Identity**
   - Accept subject token (user) + actor token (service)
   - Issue delegation token with both identities
   - Add `act` claim with nested actor identity

2. **Update Secrets service to recognize delegation tokens**
   - Log both subject and actor
   - Apply intersection of permissions

#### 4.3.2 Break-Glass Access

1. **Add break-glass role and token type**
2. **Implement break-glass workflow**
   - Requires approval (separate secure channel)
   - Short-lived token (max 4 hours)
   - Mandatory audit trail
   - Notification to security team

3. **Add break-glass detection in Secrets service**

#### 4.3.3 Cache Security Enhancements

1. **Reduce cache TTL to 2 minutes**
2. **Add cache clearing on shutdown**
3. **Consider encrypted in-memory cache for sensitive environments**

---

## 5. Endpoint Specifications

### 5.1 Updated GET /api/v1/secrets/{secretName}

**Request:**
```http
GET /api/v1/secrets/discord-client-id
Authorization: Bearer {jwt}
X-Request-ID: {correlation-id}
```

**JWT Claims Required:**
```json
{
  "sub": "user-guid",
  "org_id": "org-guid",
  "principal_type": "user",
  "permission": ["secrets:read:oauth:tenant"]
}
```

**Authorization Flow:**
1. Validate JWT signature and expiration
2. Extract `org_id` from claims
3. Determine if secret is platform or tenant-scoped:
   - Platform: Requires `secrets:read:{category}` (no `:tenant` suffix)
   - Tenant: Requires `secrets:read:{category}:tenant` AND org_id match
4. Check permission hierarchy (exact → category → wildcard)
5. Log access attempt
6. Return secret or 403

**Response (Success):**
```json
{
  "name": "discord-client-id",
  "value": "123456789012345678"
}
```

**Response (Forbidden):**
```json
{
  "type": "https://httpstatuses.io/403",
  "title": "Forbidden",
  "status": 403,
  "detail": "Access to secret 'discord-client-id' denied",
  "instance": "/api/v1/secrets/discord-client-id",
  "traceId": "00-..."
}
```

### 5.2 New POST /api/v1/secrets/audit

**Purpose:** Query audit logs for secrets access (admin only).

**Request:**
```http
POST /api/v1/secrets/audit
Authorization: Bearer {jwt}
Content-Type: application/json

{
  "startTime": "2026-01-01T00:00:00Z",
  "endTime": "2026-01-16T23:59:59Z",
  "secretName": "discord-*",
  "action": "read",
  "limit": 100
}
```

**Required Permission:** `secrets:audit:read`

**Response:**
```json
{
  "events": [
    {
      "timestamp": "2026-01-16T10:30:00Z",
      "secretName": "discord-client-id",
      "action": "read",
      "userId": "user-guid",
      "organizationId": "org-guid",
      "success": true,
      "sourceIp": "10.0.0.1",
      "correlationId": "abc-123"
    }
  ],
  "totalCount": 1,
  "hasMore": false
}
```

### 5.3 Updated Endpoint Summary

| Endpoint | Method | Changes |
|----------|--------|---------|
| `/api/v1/secrets/{name}` | GET | Add tenant validation, audit logging |
| `/api/v1/secrets/{name}` | PUT | Add tenant validation, input validation, audit logging |
| `/api/v1/secrets/{name}` | DELETE | Add tenant validation, audit logging |
| `/api/v1/secrets/{name}/rotate` | POST | Add tenant validation, audit logging |
| `/api/v1/secrets/batch` | POST | Add tenant filtering, audit logging per secret |
| `/api/v1/secrets/oauth` | GET | Add tenant filtering |
| `/api/v1/secrets/betterauth` | GET | Add tenant filtering |
| `/api/v1/secrets/infrastructure` | GET | Platform-only, no tenant prefix |
| `/api/v1/secrets/audit` | POST | **NEW** - Audit log query |

---

## 6. Service Layer Changes

### 6.1 New Files

#### `src/Dhadgar.Secrets/Authorization/ISecretsAuthorizationService.cs`

```csharp
public interface ISecretsAuthorizationService
{
    Task<AuthorizationResult> AuthorizeSecretAccessAsync(
        ClaimsPrincipal user,
        string secretName,
        SecretAction action,
        CancellationToken ct = default);
}

public enum SecretAction
{
    Read,
    Write,
    Rotate,
    Delete,
    List
}

public record AuthorizationResult(
    bool IsAuthorized,
    string? DenialReason = null,
    bool IsBreakGlass = false,
    bool IsDelegated = false);
```

#### `src/Dhadgar.Secrets/Authorization/SecretsAuthorizationService.cs`

Implements:
- Tenant isolation validation
- Permission hierarchy resolution
- Break-glass detection
- Delegation validation

#### `src/Dhadgar.Secrets/Audit/ISecretsAuditLogger.cs`

```csharp
public interface ISecretsAuditLogger
{
    Task LogAccessAsync(SecretAuditEvent evt, CancellationToken ct = default);
}

public record SecretAuditEvent(
    string SecretName,
    SecretAction Action,
    Guid? UserId,
    Guid? ServiceAccountId,
    Guid? OrganizationId,
    string? SourceIp,
    string? CorrelationId,
    bool Success,
    string? DenialReason,
    bool IsBreakGlass,
    bool IsDelegated,
    Guid? ActorUserId);
```

### 6.2 Modified Files

#### `src/Dhadgar.Secrets/Services/KeyVaultSecretProvider.cs`

Add:
- `GetSecretAsync` overload accepting `organizationId` for tenant-scoped prefix
- Validation that returned secret matches requested org scope

#### `src/Dhadgar.Secrets/Options/SecretsOptions.cs`

Add:
```csharp
public class SecretsOptions
{
    // ... existing ...

    /// <summary>
    /// Platform secrets that are NOT tenant-scoped
    /// </summary>
    public List<string> PlatformSecrets { get; set; } = new()
    {
        "betterauth-secret",
        "betterauth-exchange-private-key",
        "postgres-password",
        "rabbitmq-password",
        "redis-password"
    };

    /// <summary>
    /// Prefix pattern for tenant-scoped secrets
    /// </summary>
    public string TenantSecretPrefix { get; set; } = "{org-slug}/";
}
```

### 6.3 Identity Service Changes

#### `src/Dhadgar.Identity/Authorization/RoleDefinitions.cs`

Add secrets permissions to system roles (see Section 2.3).

#### `src/Dhadgar.Identity/Services/JwtService.cs`

Add `principal_type` claim generation:
```csharp
private Claim[] BuildClaims(User user, bool isServiceAccount)
{
    var claims = new List<Claim>
    {
        new("sub", user.Id.ToString()),
        new("principal_type", isServiceAccount ? "service" : "user"),
        // ... existing claims ...
    };
    return claims.ToArray();
}
```

---

## 7. CLI Updates

### 7.1 Tenant Context

The CLI should support tenant context for multi-tenant operations:

```bash
# Set default organization
dhadgar config set organization acme-corp

# Or specify per-command
dhadgar secret get discord-client-id --org acme-corp
```

### 7.2 Updated Commands

#### `dhadgar secret get`

```bash
dhadgar secret get <name> [--org <org-slug>] [--reveal] [--copy]

# Platform secret (admin only)
dhadgar secret get betterauth-secret --reveal

# Tenant secret
dhadgar secret get discord-client-id --org acme-corp --reveal
```

#### `dhadgar secret list`

```bash
dhadgar secret list <category> [--org <org-slug>] [--reveal]

# List OAuth secrets for tenant
dhadgar secret list oauth --org acme-corp

# List platform infrastructure secrets
dhadgar secret list infrastructure
```

#### `dhadgar secret audit` (NEW)

```bash
dhadgar secret audit [--start <datetime>] [--end <datetime>] [--secret <pattern>] [--action <action>]

# View recent access
dhadgar secret audit --start "2026-01-15" --secret "discord-*"

# View all rotations
dhadgar secret audit --action rotate
```

### 7.3 File Changes

| File | Change |
|------|--------|
| `src/Dhadgar.Cli/Commands/Secret/GetSecretCommand.cs` | Add `--org` option |
| `src/Dhadgar.Cli/Commands/Secret/SetSecretCommand.cs` | Add `--org` option |
| `src/Dhadgar.Cli/Commands/Secret/ListSecretsCommand.cs` | Add `--org` option |
| `src/Dhadgar.Cli/Commands/Secret/AuditSecretsCommand.cs` | **NEW** |
| `src/Dhadgar.Cli/Infrastructure/Clients/ISecretsApi.cs` | Add audit endpoint |

---

## 8. Testing Strategy

### 8.1 Unit Tests

#### New Test Files

| File | Coverage |
|------|----------|
| `tests/Dhadgar.Secrets.Tests/Authorization/SecretsAuthorizationServiceTests.cs` | Permission checking, hierarchy, tenant isolation |
| `tests/Dhadgar.Secrets.Tests/Audit/SecretsAuditLoggerTests.cs` | Event logging |
| `tests/Dhadgar.Secrets.Tests/Validation/SecretNameValidatorTests.cs` | Input validation |

#### Test Cases: Tenant Isolation

```csharp
[Fact]
public async Task GetSecret_DeniesAccess_WhenOrgIdMismatch()
{
    // Arrange
    var userWithOrgA = CreateUserWithOrg("org-a");
    var secretInOrgB = "org-b/oauth/discord-client-id";

    // Act
    var result = await _authService.AuthorizeSecretAccessAsync(
        userWithOrgA, secretInOrgB, SecretAction.Read);

    // Assert
    Assert.False(result.IsAuthorized);
    Assert.Equal("Organization mismatch", result.DenialReason);
}

[Fact]
public async Task GetSecret_AllowsAccess_WhenOrgIdMatches()
{
    // Arrange
    var userWithOrgA = CreateUserWithOrg("org-a");
    var secretInOrgA = "org-a/oauth/discord-client-id";

    // Act
    var result = await _authService.AuthorizeSecretAccessAsync(
        userWithOrgA, secretInOrgA, SecretAction.Read);

    // Assert
    Assert.True(result.IsAuthorized);
}

[Fact]
public async Task GetSecret_PlatformSecret_RequiresPlatformPermission()
{
    // Arrange
    var userWithTenantOnly = CreateUserWithOrg("org-a");
    var platformSecret = "betterauth-secret";

    // Act
    var result = await _authService.AuthorizeSecretAccessAsync(
        userWithTenantOnly, platformSecret, SecretAction.Read);

    // Assert
    Assert.False(result.IsAuthorized);
}
```

### 8.2 Integration Tests

#### New Test Files

| File | Coverage |
|------|----------|
| `tests/Dhadgar.Secrets.Tests/Integration/TenantIsolationIntegrationTests.cs` | End-to-end tenant isolation |
| `tests/Dhadgar.Secrets.Tests/Integration/AuditLoggingIntegrationTests.cs` | Audit events generated correctly |
| `tests/Dhadgar.Secrets.Tests/Integration/ServiceAccountIntegrationTests.cs` | Service account access patterns |

#### Test Infrastructure

```csharp
public class SecretsIntegrationTestBase : IClassFixture<SecretsWebApplicationFactory>
{
    protected readonly SecretsWebApplicationFactory Factory;
    protected HttpClient Client;

    protected void AuthenticateAsUser(string orgId, params string[] permissions)
    {
        var token = GenerateTestJwt(
            userId: Guid.NewGuid(),
            orgId: Guid.Parse(orgId),
            principalType: "user",
            permissions: permissions);

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    protected void AuthenticateAsService(string serviceName, params string[] permissions)
    {
        var token = GenerateTestJwt(
            userId: Guid.NewGuid(),
            orgId: null, // Services can be platform-wide
            principalType: "service",
            serviceName: serviceName,
            permissions: permissions);

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }
}
```

### 8.3 Security Tests

#### OWASP Testing

| Test | Implementation |
|------|----------------|
| **Broken Access Control** | Cross-tenant access attempts |
| **Injection** | Malformed secret names |
| **Security Misconfiguration** | Swagger exposure, error details |
| **Logging Failures** | Verify audit events generated |

```csharp
[Theory]
[InlineData("../../../etc/passwd")]
[InlineData("secret'; DROP TABLE secrets;--")]
[InlineData("<script>alert('xss')</script>")]
[InlineData("secret\0name")]
public async Task GetSecret_RejectsInvalidSecretNames(string maliciousName)
{
    // Arrange
    AuthenticateAsUser("org-a", "secrets:read:*:tenant");

    // Act
    var response = await Client.GetAsync($"/api/v1/secrets/{Uri.EscapeDataString(maliciousName)}");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

### 8.4 Test Coverage Targets

| Area | Target Coverage |
|------|-----------------|
| Authorization logic | 95% |
| Endpoints | 90% |
| Audit logging | 100% |
| Input validation | 100% |
| Error handling | 85% |

---

## 9. Documentation Updates

### 9.1 Files to Create/Update

| File | Type | Content |
|------|------|---------|
| `docs/secrets/README.md` | New | Secrets service overview |
| `docs/secrets/AUTHORIZATION.md` | New | Permission model, roles, tenant isolation |
| `docs/secrets/AUDIT.md` | New | Audit logging, event schema, querying |
| `docs/secrets/CLI.md` | New | CLI command reference |
| `docs/secrets/API.md` | New | OpenAPI spec with examples |
| `CLAUDE.md` | Update | Add secrets service section |

### 9.2 CLAUDE.md Updates

Add to Architecture section:

```markdown
### Secrets Service

The Secrets service provides centralized management of sensitive configuration data.

**Key Concepts:**
- **Platform secrets**: Shared across tenants (infrastructure passwords, auth keys)
- **Tenant secrets**: Scoped to specific organizations (OAuth credentials)

**Permission Model:**
- `secrets:read:{category}:tenant` - Tenant-scoped read
- `secrets:write:{category}:tenant` - Tenant-scoped write
- `secrets:read:{category}` - Platform-level read (admin only)

**Security Features:**
- Tenant isolation enforced at service level
- All access logged to security audit
- Service accounts distinguished from users
- Break-glass emergency access available

**Integration:**
- Depends on: Identity (auth), Gateway (routing)
- Depended on by: All services needing secrets
```

### 9.3 OpenAPI Documentation

Update Swagger to include:
- Security schemes (JWT Bearer)
- Permission requirements per endpoint
- Request/response examples
- Error response schemas

---

## 10. Security Checklist

### Pre-Production Checklist

- [ ] **Tenant Isolation**
  - [ ] `org_id` validated on all secret access
  - [ ] Platform secrets require elevated permissions
  - [ ] Cross-tenant access blocked and logged

- [ ] **Authentication**
  - [ ] JWT validation strict (issuer, audience, expiration)
  - [ ] HTTPS required in production
  - [ ] Token lifetime appropriate for sensitivity

- [ ] **Authorization**
  - [ ] Permission hierarchy implemented
  - [ ] Service accounts distinguished
  - [ ] Delegation tokens validated

- [ ] **Audit**
  - [ ] All access logged with context
  - [ ] Denials logged with reason
  - [ ] Break-glass flagged in logs

- [ ] **Input Validation**
  - [ ] Secret names validated against regex
  - [ ] Size limits enforced
  - [ ] Injection patterns rejected

- [ ] **Error Handling**
  - [ ] Azure errors not exposed to clients
  - [ ] Stack traces not in production responses
  - [ ] Consistent Problem Details format

- [ ] **Rate Limiting**
  - [ ] Read operations limited
  - [ ] Write operations more restrictive
  - [ ] Anomaly patterns logged

- [ ] **Testing**
  - [ ] Unit tests for authorization
  - [ ] Integration tests for tenant isolation
  - [ ] Security tests for OWASP top 10

---

## Appendix A: Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Keep Secrets service stateless | Consistent with architecture, simplifies scaling | 2026-01-16 |
| Store ACLs in Identity, not Secrets | Single source of truth for authorization | 2026-01-16 |
| Use permission claims in JWT | Avoids per-request calls to Identity | 2026-01-16 |
| Tenant prefix in secret names | Natural Azure Key Vault alignment | 2026-01-16 |
| Platform secrets without prefix | Distinguished from tenant secrets | 2026-01-16 |

## Appendix B: References

- [Azure Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [Azure Key Vault RBAC](https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide)
- [RFC 8693 - OAuth 2.0 Token Exchange](https://datatracker.ietf.org/doc/html/rfc8693)
- [OWASP Top 10](https://owasp.org/Top10/)

---

**Document Prepared By:** Claude Code Analysis
**Review Required By:** Security Team, Platform Architecture Team
