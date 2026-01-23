# Dhadgar.Identity

Identity and access management service for Meridian Console. This is the central authentication and authorization service that handles user lifecycle, organization management, role-based access control (RBAC), OAuth provider integration, and multi-tenancy for the entire platform.

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Database Schema](#database-schema)
- [Authentication Flow](#authentication-flow)
- [Authorization Model](#authorization-model)
- [API Endpoints](#api-endpoints)
- [OAuth Providers](#oauth-providers)
- [Webhooks](#webhooks)
- [Event Publishing](#event-publishing)
- [Multi-Tenancy](#multi-tenancy)
- [Audit Logging](#audit-logging)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Security Considerations](#security-considerations)
- [Related Documentation](#related-documentation)

---

## Overview

The Identity service is one of the most critical components of Meridian Console. It serves as the platform's identity provider (IdP) and implements:

- **Authentication**: Token exchange from Better Auth, JWT issuance, refresh token management
- **Authorization**: Role-based access control (RBAC) with fine-grained claims
- **User Management**: User lifecycle, profile management, account deletion
- **Organization Management**: Multi-tenant organization creation, membership, ownership transfer
- **OAuth Integration**: Gaming platform account linking (Steam, Battle.net, Epic, Xbox)
- **Audit Trail**: Comprehensive logging of security-sensitive operations

### Key Design Principles

1. **External Authentication**: Primary authentication is delegated to Better Auth (social OAuth). Identity service exchanges Better Auth tokens for platform JWTs.
2. **Multi-Tenancy First**: Users can belong to multiple organizations with different roles and permissions in each.
3. **Stateless JWT**: Access tokens are short-lived JWTs (15 minutes default) with embedded permissions.
4. **Security by Default**: Rate limiting, replay protection, signature validation, and audit logging throughout.

### Service Dependencies

| Dependency | Purpose |
|------------|---------|
| PostgreSQL | Primary data store for users, organizations, memberships |
| Redis | Exchange token replay protection, caching |
| RabbitMQ | Event publishing via MassTransit |
| Azure Key Vault | Production certificate and secret storage |

---

## Tech Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime and framework |
| ASP.NET Core | 10.0 | Minimal API web framework |
| Entity Framework Core | 10.0 | ORM and migrations |
| PostgreSQL | 15+ | Primary database |
| Redis | 7+ | Token replay store |
| OpenIddict | 5.x | OpenID Connect server |
| MassTransit | 8.3.6 | Message bus abstraction |
| StackExchange.Redis | 2.x | Redis client |
| Azure.Identity | 1.x | Azure authentication |
| Azure.Security.KeyVault | 4.x | Certificate/secret management |
| OpenTelemetry | 1.14.0 | Distributed tracing |

### NuGet Packages

Key packages used (see `Directory.Packages.props` for versions):

```xml
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
<PackageReference Include="OpenIddict.AspNetCore" />
<PackageReference Include="OpenIddict.EntityFrameworkCore" />
<PackageReference Include="StackExchange.Redis" />
<PackageReference Include="AspNet.Security.OAuth.BattleNet" />
<PackageReference Include="AspNet.Security.OpenId.Steam" />
<PackageReference Include="MassTransit.RabbitMQ" />
```

---

## Quick Start

### Prerequisites

- .NET 10 SDK
- PostgreSQL (localhost:5432)
- Redis (localhost:6379)
- RabbitMQ (localhost:5672)

### Running Locally

```bash
# Start infrastructure
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# Run the service
dotnet run --project src/Dhadgar.Identity

# Or with watch mode for hot reload
dotnet watch --project src/Dhadgar.Identity
```

The service runs on `http://localhost:5000` by default.

### Swagger UI

Access the API documentation at: `http://localhost:5000/swagger`

### Quick Verification

```bash
# Check service health
curl http://localhost:5000/healthz

# Get service info
curl http://localhost:5000/

# Response:
# {"service":"Dhadgar.Identity","message":"Hello from Dhadgar.Identity!"}
```

---

## Configuration

### Configuration Hierarchy

1. `appsettings.json` - Base configuration
2. `appsettings.Development.json` - Development overrides
3. Environment variables - Runtime overrides
4. User secrets - Local development secrets
5. Azure Key Vault - Production secrets

### Required Configuration

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Postgres` | PostgreSQL connection string | localhost:5432 |
| `Redis:ConnectionString` | Redis connection string | localhost:6379 |
| `Auth:Issuer` | JWT issuer URL | - |
| `Auth:Audience` | JWT audience | meridian-api |

### Full Configuration Reference

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password="
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "RabbitMq": {
    "Host": "localhost",
    "Username": "dhadgar",
    "Password": ""
  },
  "SecretsService": {
    "Url": "http://localhost:5000"
  },
  "Auth": {
    "Issuer": "https://meridianconsole.com/api/v1/identity",
    "Audience": "meridian-api",
    "AccessTokenLifetimeSeconds": 900,
    "RefreshTokenLifetimeDays": 7,
    "SigningKeyKid": "meridian-identity-v1",
    "SigningKeyPath": "",
    "SigningKeyPem": "",
    "UseDevelopmentCertificates": false,
    "KeyVault": {
      "VaultUri": "https://mc-core.vault.azure.net/",
      "SigningCertName": "identity-signing-cert",
      "EncryptionCertName": "identity-encryption-cert",
      "ExchangePublicKeyName": "identity-exchange-public-key",
      "JwtSigningKeyName": "identity-jwt-signing-key"
    },
    "Exchange": {
      "Issuer": "https://meridianconsole.com/api/v1/betterauth",
      "Audience": "https://meridianconsole.com/api/v1/identity/exchange",
      "PublicKeyPem": "",
      "PublicKeyPath": ""
    },
    "EmailVerification": {
      "RequireVerifiedEmail": false,
      "OperationsRequiringVerification": ["billing", "org:delete"]
    }
  },
  "OAuth": {
    "Steam": {
      "ApplicationKey": ""
    },
    "BattleNet": {
      "ClientId": "",
      "ClientSecret": "",
      "Region": "America"
    },
    "Epic": {
      "ClientId": "",
      "ClientSecret": "",
      "AuthorizationEndpoint": "",
      "TokenEndpoint": "",
      "UserInformationEndpoint": ""
    },
    "Xbox": {
      "ClientId": "",
      "ClientSecret": ""
    },
    "AllowedRedirectHosts": [
      "meridianconsole.com",
      "panel.meridianconsole.com",
      "localhost"
    ]
  },
  "Webhooks": {
    "BetterAuthSecretName": "better-auth-webhook-secret",
    "SignatureHeader": "X-Webhook-Signature",
    "MaxTimestampAgeSeconds": 300,
    "SecretCacheMinutes": 60
  },
  "OpenIddict": {
    "DevClient": {
      "Enabled": true,
      "ClientId": "dev-client",
      "ClientSecret": "dev-secret",
      "RedirectUris": []
    },
    "ServiceAccounts": {
      "SecretsService": {
        "Enabled": true,
        "ClientId": "secrets-service"
      },
      "BetterAuthService": {
        "Enabled": true,
        "ClientId": "betterauth-service"
      },
      "BetterAuthWif": {
        "Enabled": true,
        "ClientId": "betterauth-client"
      }
    },
    "Wif": {
      "Audience": "api://AzureADTokenExchange"
    }
  },
  "Database": {
    "AutoMigrate": false
  },
  "OpenTelemetry": {
    "OtlpEndpoint": ""
  }
}
```

### Development Mode

Set `Auth:UseDevelopmentCertificates` to `true` to use ephemeral certificates (no Key Vault required):

```bash
# Using user-secrets for local development
dotnet user-secrets set "Auth:UseDevelopmentCertificates" "true" --project src/Dhadgar.Identity
```

### User Secrets Setup

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Identity

# Set connection strings (avoid committing passwords)
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=your-password" --project src/Dhadgar.Identity

# Set Redis
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379,password=your-password" --project src/Dhadgar.Identity

# Enable development certificates
dotnet user-secrets set "Auth:UseDevelopmentCertificates" "true" --project src/Dhadgar.Identity
```

---

## Architecture

### High-Level Architecture

```
                                    +------------------+
                                    |   Better Auth    |
                                    | (External Auth)  |
                                    +--------+---------+
                                             |
                                             | Exchange Token
                                             v
+-------------+    JWT Token    +------------+------------+
|   Client    | <-------------> |  Dhadgar.Identity       |
| (Panel/CLI) |                 |  - Token Exchange       |
+-------------+                 |  - User Management      |
                                |  - Org Management       |
                                |  - RBAC                 |
                                +------------+------------+
                                             |
              +------------------------------+------------------------------+
              |                              |                              |
              v                              v                              v
     +--------+--------+          +----------+---------+          +--------+--------+
     |   PostgreSQL    |          |      Redis         |          |    RabbitMQ     |
     |  - Users        |          |  - Replay Store    |          |  - Events       |
     |  - Organizations|          |  - Cache           |          |  - Notifications|
     |  - Memberships  |          +--------------------+          +-----------------+
     |  - Audit Events |
     +------------------+
```

### Internal Component Structure

```
src/Dhadgar.Identity/
├── Program.cs                     # Application entry point, DI configuration
├── Hello.cs                       # Service banner
├── Authentication/
│   └── AuthSchemes.cs             # Authentication scheme constants
├── Authorization/
│   └── RoleDefinitions.cs         # Role hierarchy and implied claims
├── Data/
│   ├── IdentityDbContext.cs       # EF Core DbContext
│   ├── IdentityDbContextFactory.cs # Design-time factory for migrations
│   ├── Entities/                  # Domain entities
│   │   ├── User.cs
│   │   ├── Organization.cs
│   │   ├── UserOrganization.cs
│   │   ├── OrganizationRole.cs
│   │   ├── UserOrganizationClaim.cs
│   │   ├── LinkedAccount.cs
│   │   ├── RefreshToken.cs
│   │   ├── ClaimDefinition.cs
│   │   └── AuditEvent.cs
│   ├── Configuration/             # EF Core configurations
│   │   ├── UserConfiguration.cs
│   │   ├── OrganizationConfiguration.cs
│   │   └── ...
│   └── Migrations/                # EF Core migrations
├── Endpoints/                     # Minimal API endpoints
│   ├── TokenExchangeEndpoint.cs
│   ├── OrganizationEndpoints.cs
│   ├── MembershipEndpoints.cs
│   ├── UserEndpoints.cs
│   ├── RoleEndpoints.cs
│   ├── SessionEndpoints.cs
│   ├── MeEndpoints.cs
│   ├── ActivityEndpoints.cs
│   ├── SearchEndpoints.cs
│   ├── OAuthEndpoints.cs
│   ├── WebhookEndpoint.cs
│   ├── InternalEndpoints.cs
│   ├── MfaPolicyEndpoints.cs
│   └── EndpointHelpers.cs
├── Extensions/
│   └── ClaimsPrincipalExtensions.cs
├── OAuth/                         # OAuth provider handlers
│   ├── OAuthProviderRegistry.cs
│   ├── OAuthLinkingHandler.cs
│   ├── OAuthSecretProvider.cs
│   ├── EpicGamesOAuthHandler.cs
│   └── MockOAuthHandler.cs
├── Options/
│   └── AuthOptions.cs             # Configuration POCOs
├── Readiness/
│   └── IdentityReadinessCheck.cs  # Health check
└── Services/                      # Business logic
    ├── TokenExchangeService.cs
    ├── JwtService.cs
    ├── RefreshTokenService.cs
    ├── PermissionService.cs
    ├── OrganizationService.cs
    ├── OrganizationSwitchService.cs
    ├── MembershipService.cs
    ├── UserService.cs
    ├── RoleService.cs
    ├── LinkedAccountService.cs
    ├── AuditService.cs
    ├── IdentityEventPublisher.cs
    ├── SigningKeyProvider.cs
    ├── ExchangeTokenValidator.cs
    ├── ExchangeTokenReplayStore.cs
    ├── ClientAssertionService.cs
    ├── AzureWifTokenHandler.cs
    ├── TokenCleanupService.cs
    ├── InvitationCleanupService.cs
    ├── CachedPermissionService.cs
    ├── WebhookSecretProvider.cs
    └── ServiceResult.cs
```

### Request Pipeline

```
Request → Rate Limiter → Correlation Middleware → Problem Details Middleware
       → Request Logging → Authentication → Authorization → Endpoint Handler
       → Response
```

Key middleware components (from `Dhadgar.ServiceDefaults`):

1. **CorrelationMiddleware**: Adds correlation, request, and trace IDs
2. **ProblemDetailsMiddleware**: Transforms exceptions to RFC 7807 responses
3. **RequestLoggingMiddleware**: Logs requests with correlation context

---

## Database Schema

### Entity Relationship Diagram

```
+------------------+       +-------------------+       +------------------+
|      User        |       | UserOrganization  |       |  Organization    |
+------------------+       +-------------------+       +------------------+
| Id (PK)          |<----->| UserId (FK)       |       | Id (PK)          |
| ExternalAuthId   |       | OrganizationId(FK)|<----->| Name             |
| Email            |       | Role              |       | Slug             |
| DisplayName      |       | IsActive          |       | OwnerId (FK)     |
| AvatarUrl        |       | JoinedAt          |       | Settings (JSON)  |
| EmailVerified    |       | LeftAt            |       | CreatedAt        |
| PreferredOrgId   |       | InvitedByUserId   |       | UpdatedAt        |
| HasPasskeys      |       | InvitationExpires |       | DeletedAt        |
| LastAuthenticatedAt      | InvitationAccepted|       | Version          |
| CreatedAt        |       +-------------------+       +------------------+
| UpdatedAt        |               |
| DeletedAt        |               |
| Version          |               v
+------------------+       +------------------------+
        |                  | UserOrganizationClaim  |
        |                  +------------------------+
        v                  | Id (PK)                |
+------------------+       | UserOrganizationId (FK)|
| LinkedAccount    |       | ClaimType (Grant/Deny) |
+------------------+       | ClaimValue             |
| Id (PK)          |       | ResourceType           |
| UserId (FK)      |       | ResourceId             |
| Provider         |       | GrantedAt              |
| ProviderAccountId|       | GrantedByUserId        |
| ProviderMetadata |       | ExpiresAt              |
| LinkedAt         |       +------------------------+
| LastUsedAt       |
+------------------+

+------------------+       +------------------+       +------------------+
| OrganizationRole |       |  RefreshToken    |       |   AuditEvent     |
+------------------+       +------------------+       +------------------+
| Id (PK)          |       | Id (PK)          |       | Id (PK)          |
| OrganizationId   |       | UserId (FK)      |       | EventType        |
| Name             |       | TokenHash        |       | UserId           |
| NormalizedName   |       | OrganizationId   |       | OrganizationId   |
| Description      |       | IssuedAt         |       | ActorUserId      |
| Permissions[]    |       | ExpiresAt        |       | TargetType       |
| CreatedAt        |       | RevokedAt        |       | TargetId         |
| UpdatedAt        |       | DeviceInfo       |       | ClientIp         |
+------------------+       +------------------+       | UserAgent        |
                                                      | Details (JSON)   |
+------------------+                                  | CorrelationId    |
| ClaimDefinition  |                                  | OccurredAtUtc    |
+------------------+                                  +------------------+
| Id (PK)          |
| Name             |
| Description      |
| Category         |
| IsSystemClaim    |
| CreatedAt        |
+------------------+
```

### Entity Details

#### User

Primary user record backed by ASP.NET Core Identity.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `ExternalAuthId` | `string(255)` | Better Auth user ID (sub claim) |
| `Email` | `string` | User's email address |
| `NormalizedEmail` | `string` | Uppercase normalized email for lookups |
| `DisplayName` | `string(200)` | Display name (optional) |
| `AvatarUrl` | `string(500)` | Profile picture URL |
| `EmailVerified` | `bool` | Whether email is verified |
| `PreferredOrganizationId` | `Guid?` | Sticky organization preference |
| `HasPasskeysRegistered` | `bool` | Synced from Better Auth webhooks |
| `LastPasskeyAuthAt` | `DateTime?` | Last passkey authentication |
| `LastAuthenticatedAt` | `DateTime?` | Last successful authentication |
| `CreatedAt` | `DateTime` | Account creation timestamp |
| `UpdatedAt` | `DateTime?` | Last update timestamp |
| `DeletedAt` | `DateTime?` | Soft-delete timestamp |
| `Version` | `uint` | Optimistic concurrency (PostgreSQL xmin) |

#### Organization

Multi-tenant organization container.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `Name` | `string(200)` | Organization display name |
| `Slug` | `string(100)` | URL-safe identifier (e.g., "acme-corp") |
| `OwnerId` | `Guid` | FK to owner User |
| `Settings` | `JSON` | Embedded settings object |
| `CreatedAt` | `DateTime` | Creation timestamp |
| `UpdatedAt` | `DateTime?` | Last update |
| `DeletedAt` | `DateTime?` | Soft-delete timestamp |
| `Version` | `uint` | Optimistic concurrency |

**OrganizationSettings** (embedded JSON):

```csharp
public sealed class OrganizationSettings
{
    public bool AllowMemberInvites { get; set; } = true;
    public bool RequireEmailVerification { get; set; } = true;
    public int MaxMembers { get; set; } = 10;
    public Dictionary<string, string>? CustomSettings { get; set; }
}
```

#### UserOrganization

Join table for user-organization membership with role assignment.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `UserId` | `Guid` | FK to User |
| `OrganizationId` | `Guid` | FK to Organization |
| `Role` | `string(50)` | Role name (system or custom) |
| `IsActive` | `bool` | Whether membership is active |
| `JoinedAt` | `DateTime` | When user joined |
| `LeftAt` | `DateTime?` | When user left (soft leave) |
| `InvitedByUserId` | `Guid?` | Who sent the invitation |
| `InvitationAcceptedAt` | `DateTime?` | When invitation was accepted |
| `InvitationExpiresAt` | `DateTime?` | Invitation expiration time |

#### OrganizationRole

Custom roles defined per organization.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `OrganizationId` | `Guid` | FK to Organization |
| `Name` | `string(50)` | Role display name |
| `NormalizedName` | `string(50)` | Uppercase for lookups |
| `Description` | `string(500)` | Role description |
| `Permissions` | `string[]` | Array of permission strings |
| `CreatedAt` | `DateTime` | Creation timestamp |
| `UpdatedAt` | `DateTime?` | Last update |

#### UserOrganizationClaim

Fine-grained permission grants or denials per user per organization.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `UserOrganizationId` | `Guid` | FK to UserOrganization |
| `ClaimType` | `enum` | Grant (1) or Deny (2) |
| `ClaimValue` | `string(100)` | Permission name (e.g., "servers:delete") |
| `ResourceType` | `string(50)` | Optional scoping (e.g., "server") |
| `ResourceId` | `Guid?` | Optional specific resource |
| `GrantedAt` | `DateTime` | When claim was added |
| `GrantedByUserId` | `Guid` | Who granted the claim |
| `ExpiresAt` | `DateTime?` | Optional expiration |

#### LinkedAccount

OAuth provider account linkages for gaming platforms.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `UserId` | `Guid` | FK to User |
| `Provider` | `string(50)` | Provider name (steam, discord, etc.) |
| `ProviderAccountId` | `string(255)` | Provider's user ID |
| `ProviderMetadata` | `JSON` | Avatar, username, extra data |
| `LinkedAt` | `DateTime` | When account was linked |
| `LastUsedAt` | `DateTime?` | Last authentication via this provider |

#### RefreshToken

Refresh token tracking for session management.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `UserId` | `Guid` | FK to User |
| `TokenHash` | `string(128)` | SHA256 hash of refresh token |
| `OrganizationId` | `Guid` | Organization context |
| `IssuedAt` | `DateTime` | Token issue time |
| `ExpiresAt` | `DateTime` | Token expiration |
| `RevokedAt` | `DateTime?` | When token was revoked |
| `DeviceInfo` | `string(500)` | Optional device info for audit |

#### AuditEvent

Persistent audit trail for security-sensitive operations.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `EventType` | `string(100)` | Event type (e.g., "user.created") |
| `UserId` | `Guid?` | Affected user |
| `OrganizationId` | `Guid?` | Organization context |
| `ActorUserId` | `Guid?` | Who performed the action |
| `TargetType` | `string(50)` | Resource type affected |
| `TargetId` | `Guid?` | Specific resource ID |
| `ClientIp` | `string(45)` | Client IP address |
| `UserAgent` | `string(500)` | User agent string |
| `Details` | `JSON` | Event-specific details |
| `CorrelationId` | `string(50)` | Distributed trace correlation |
| `OccurredAtUtc` | `DateTime` | Event timestamp |

#### ClaimDefinition

System-defined and custom permission definitions (seeded data).

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Primary key |
| `Name` | `string(100)` | Claim name (e.g., "servers:read") |
| `Description` | `string(500)` | Human-readable description |
| `Category` | `string(50)` | Grouping category |
| `IsSystemClaim` | `bool` | Whether system-defined (cannot delete) |
| `CreatedAt` | `DateTime` | Creation timestamp |

### Database Migrations

```bash
# Add a new migration
dotnet ef migrations add MigrationName \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity

# Remove last migration (if not applied)
dotnet ef migrations remove \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity
```

### Auto-Migration

In Development mode or when `Database:AutoMigrate` is `true`, migrations are applied automatically on startup.

---

## Authentication Flow

### Token Exchange Flow (Primary)

The primary authentication flow uses Better Auth for social OAuth, then exchanges for platform JWTs.

```
┌─────────┐     ┌────────────┐     ┌─────────────┐     ┌──────────────┐
│  User   │     │   Panel    │     │ Better Auth │     │   Identity   │
└────┬────┘     └─────┬──────┘     └──────┬──────┘     └──────┬───────┘
     │                │                   │                   │
     │  Click Login   │                   │                   │
     │───────────────>│                   │                   │
     │                │                   │                   │
     │                │  Redirect OAuth   │                   │
     │                │──────────────────>│                   │
     │                │                   │                   │
     │       OAuth Flow (Google, GitHub, etc.)                │
     │<──────────────────────────────────>│                   │
     │                │                   │                   │
     │                │   Exchange Token  │                   │
     │                │<──────────────────│                   │
     │                │                   │                   │
     │                │          POST /exchange               │
     │                │─────────────────────────────────────>│
     │                │                   │                   │
     │                │                   │  Validate Token   │
     │                │                   │  Create/Find User │
     │                │                   │  Calculate Perms  │
     │                │                   │  Issue JWT        │
     │                │                   │                   │
     │                │     { accessToken, refreshToken }     │
     │                │<─────────────────────────────────────│
     │                │                   │                   │
     │   Logged In    │                   │                   │
     │<───────────────│                   │                   │
```

### Token Exchange Request

```http
POST /exchange
Content-Type: application/json

{
  "exchangeToken": "eyJhbGciOiJFUzI1NiIs..."
}
```

### Token Exchange Response

```json
{
  "accessToken": "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "expiresIn": 900,
  "userId": "550e8400-e29b-41d4-a716-446655440000",
  "organizationId": "660e8400-e29b-41d4-a716-446655440000"
}
```

### JWT Claims Structure

Access tokens include the following claims:

```json
{
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "org_id": "660e8400-e29b-41d4-a716-446655440000",
  "email": "user@example.com",
  "role": "admin",
  "email_verified": "true",
  "principal_type": "user",
  "client_app": "panel",
  "permission": ["servers:read", "servers:write", "nodes:read"],
  "iss": "https://meridianconsole.com/api/v1/identity/",
  "aud": "meridian-api",
  "exp": 1704067200,
  "iat": 1704066300
}
```

### Refresh Token Flow

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token&refresh_token=dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...
```

Response:

```json
{
  "access_token": "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refresh_token": "bmV3IHJlZnJlc2ggdG9rZW4...",
  "token_type": "Bearer",
  "expires_in": 900
}
```

### OpenID Connect Flows

The service also supports standard OAuth2/OIDC flows via OpenIddict:

| Flow | Grant Type | Use Case |
|------|------------|----------|
| Client Credentials | `client_credentials` | Service-to-service authentication |
| Authorization Code + PKCE | `authorization_code` | Interactive user login (future) |
| Refresh Token | `refresh_token` | Token renewal |

### Client Credentials Example

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=secrets-service
&client_secret=your-client-secret
&scope=wif
```

---

## Authorization Model

### Role Hierarchy

The service implements a hierarchical RBAC model with four system roles:

| Role | Description | Can Assign |
|------|-------------|------------|
| `owner` | Full control over organization | admin, operator, viewer |
| `admin` | Manage servers and members | operator, viewer |
| `operator` | Operate servers and manage files | - |
| `viewer` | Read-only access | - |

### Role-Implied Claims

Each role automatically grants a set of permissions:

```csharp
["owner"] = new RoleDefinition
{
    ImpliedClaims = new[]
    {
        "org:read", "org:write", "org:delete", "org:billing",
        "members:read", "members:invite", "members:remove", "members:roles",
        "servers:read", "servers:write", "servers:delete",
        "servers:start", "servers:stop", "servers:restart",
        "nodes:read", "nodes:manage",
        "files:read", "files:write", "files:delete",
        "mods:read", "mods:write", "mods:delete",
        "secrets:read:oauth", "secrets:read:infrastructure"
    }
}

["admin"] = new RoleDefinition
{
    ImpliedClaims = new[]
    {
        "org:read",
        "members:read", "members:invite", "members:remove",
        "servers:read", "servers:write", "servers:delete",
        "servers:start", "servers:stop", "servers:restart",
        "nodes:read",
        "files:read", "files:write", "files:delete",
        "mods:read", "mods:write", "mods:delete",
        "secrets:read:oauth"
    }
}

["operator"] = new RoleDefinition
{
    ImpliedClaims = new[]
    {
        "org:read", "members:read",
        "servers:read", "servers:write",
        "servers:start", "servers:stop", "servers:restart",
        "nodes:read",
        "files:read", "files:write",
        "mods:read", "mods:write"
    }
}

["viewer"] = new RoleDefinition
{
    ImpliedClaims = new[]
    {
        "org:read", "members:read",
        "servers:read", "nodes:read",
        "files:read", "mods:read"
    }
}
```

### Platform Roles

Special roles for platform-level administration:

| Role | Description |
|------|-------------|
| `platform-admin` | Full platform administration including secrets |
| `secrets-admin` | Full control over platform secrets |
| `secrets-reader` | Read-only access to platform secrets |

### Custom Claims (Grant/Deny)

Fine-grained permissions can be added or removed per user:

- **Grant**: Add a permission beyond the role's implied claims
- **Deny**: Remove a permission even if role-implied

Example: Give a `viewer` user permission to start servers:

```http
POST /organizations/{orgId}/members/{memberId}/claims
Content-Type: application/json

{
  "claimType": 1,
  "claimValue": "servers:start"
}
```

### Permission Categories

| Category | Permissions |
|----------|-------------|
| `organization` | `org:read`, `org:write`, `org:delete`, `org:billing` |
| `members` | `members:read`, `members:invite`, `members:remove`, `members:roles` |
| `servers` | `servers:read`, `servers:write`, `servers:delete`, `servers:start`, `servers:stop`, `servers:restart` |
| `nodes` | `nodes:read`, `nodes:manage` |
| `files` | `files:read`, `files:write`, `files:delete` |
| `mods` | `mods:read`, `mods:write`, `mods:delete` |

### Permission Calculation

Permissions are calculated at token issuance time:

1. Get user's role in the organization
2. Look up role definition (system or custom)
3. Add all role-implied claims
4. Apply custom claims (Grant adds, Deny removes)
5. Check claim expiration
6. Embed final permission set in JWT

```csharp
public async Task<IReadOnlyCollection<string>> CalculatePermissionsAsync(
    Guid userId, Guid organizationId, CancellationToken ct)
{
    var membership = await GetMembershipAsync(userId, organizationId);
    var permissions = new HashSet<string>();

    // Add role-implied claims
    var roleDefinition = RoleDefinitions.GetRole(membership.Role);
    permissions.UnionWith(roleDefinition.ImpliedClaims);

    // Apply custom claims
    var customClaims = await GetCustomClaimsAsync(membership.Id);
    foreach (var claim in customClaims)
    {
        if (claim.ClaimType == ClaimType.Grant)
            permissions.Add(claim.ClaimValue);
        else if (claim.ClaimType == ClaimType.Deny)
            permissions.Remove(claim.ClaimValue);
    }

    return permissions;
}
```

---

## API Endpoints

### Health and Info

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/` | GET | None | Service info |
| `/hello` | GET | None | Hello message |
| `/healthz` | GET | None | Health check |
| `/ready` | GET | None | Readiness probe |

### Authentication

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/exchange` | POST | None | Exchange Better Auth token for JWT |
| `/logout` | POST | Bearer | Revoke current session |
| `/connect/token` | POST | Varies | OpenIddict token endpoint |

#### POST /exchange

Exchange a Better Auth exchange token for platform JWT.

**Request:**

```json
{
  "exchangeToken": "eyJhbGciOiJFUzI1NiIs..."
}
```

**Response (Success - 200):**

```json
{
  "accessToken": "eyJhbGciOiJFUzI1NiIs...",
  "refreshToken": "cmVmcmVzaF90b2tlbl9oZXJl...",
  "expiresIn": 900,
  "userId": "550e8400-e29b-41d4-a716-446655440000",
  "organizationId": "660e8400-e29b-41d4-a716-446655440000"
}
```

**Error Responses:**

| Status | Error | Description |
|--------|-------|-------------|
| 400 | `missing_exchange_token` | Token not provided |
| 400 | `token_already_used` | Replay attack detected |
| 401 | - | Invalid token |
| 403 | `email_not_verified` | Email verification required |

### Organizations

| Endpoint | Method | Auth | Permission | Description |
|----------|--------|------|------------|-------------|
| `/organizations` | GET | Bearer | - | List user's organizations |
| `/organizations` | POST | Bearer | - | Create organization |
| `/organizations/{id}` | GET | Bearer | `org:read` | Get organization details |
| `/organizations/{id}` | PATCH | Bearer | `org:write` | Update organization |
| `/organizations/{id}` | DELETE | Bearer | `org:delete` | Soft-delete organization |
| `/organizations/{id}/switch` | POST | Bearer | - | Switch active organization |
| `/organizations/{id}/transfer-ownership` | POST | Bearer | Owner | Transfer ownership |
| `/organizations/search` | GET | Bearer | - | Search organizations |

#### POST /organizations

Create a new organization.

**Request:**

```json
{
  "name": "My Game Server Company",
  "slug": "my-game-servers"
}
```

**Response (201):**

```json
{
  "id": "770e8400-e29b-41d4-a716-446655440000"
}
```

#### POST /organizations/{id}/switch

Switch to a different organization and get new tokens.

**Response:**

```json
{
  "accessToken": "eyJhbGciOiJFUzI1NiIs...",
  "refreshToken": "bmV3X3JlZnJlc2hfdG9rZW4...",
  "expiresIn": 900,
  "organizationId": "770e8400-e29b-41d4-a716-446655440000",
  "permissions": ["servers:read", "servers:write", "nodes:read"]
}
```

### Memberships

| Endpoint | Method | Auth | Permission | Description |
|----------|--------|------|------------|-------------|
| `/organizations/{id}/members` | GET | Bearer | `members:read` | List members |
| `/organizations/{id}/members/invite` | POST | Bearer | `members:invite` | Invite member |
| `/organizations/{id}/members/accept` | POST | Bearer | - | Accept invitation |
| `/organizations/{id}/members/reject` | POST | Bearer | - | Reject invitation |
| `/organizations/{id}/members/{memberId}` | DELETE | Bearer | `members:remove` | Remove member |
| `/organizations/{id}/members/{memberId}/role` | POST | Bearer | `members:roles` | Assign role |
| `/organizations/{id}/members/{memberId}/claims` | GET | Bearer | `members:read` | List custom claims |
| `/organizations/{id}/members/{memberId}/claims` | POST | Bearer | `members:roles` | Add custom claim |
| `/organizations/{id}/members/{memberId}/claims/{claimId}` | DELETE | Bearer | `members:roles` | Remove claim |
| `/organizations/{id}/members/bulk-invite` | POST | Bearer | `members:invite` | Bulk invite |
| `/organizations/{id}/members/bulk-remove` | POST | Bearer | `members:remove` | Bulk remove |
| `/organizations/{id}/invitations/{userId}` | DELETE | Bearer | `members:invite` | Withdraw invitation |

#### POST /organizations/{id}/members/invite

Invite a user to the organization.

**Request:**

```json
{
  "email": "newuser@example.com",
  "role": "operator"
}
```

**Response (200):**

```json
{
  "membershipId": "880e8400-e29b-41d4-a716-446655440000"
}
```

### Users

| Endpoint | Method | Auth | Permission | Description |
|----------|--------|------|------------|-------------|
| `/organizations/{id}/users` | GET | Bearer | `members:read` | List users |
| `/organizations/{id}/users/{userId}` | GET | Bearer | `members:read` | Get user details |
| `/organizations/{id}/users` | POST | Bearer | `members:invite` | Create user |
| `/organizations/{id}/users/{userId}` | PATCH | Bearer | `members:invite` | Update user |
| `/organizations/{id}/users/{userId}` | DELETE | Bearer | `members:remove` | Delete user |
| `/organizations/{id}/users/{userId}/linked-accounts/{accountId}` | DELETE | Bearer | `members:roles` | Unlink OAuth |
| `/organizations/{id}/users/search` | GET | Bearer | `members:read` | Search users |

### Roles

| Endpoint | Method | Auth | Permission | Description |
|----------|--------|------|------------|-------------|
| `/organizations/{id}/roles` | GET | Bearer | `members:read` | List roles |
| `/organizations/{id}/roles/{roleId}` | GET | Bearer | `members:read` | Get role |
| `/organizations/{id}/roles` | POST | Bearer | `members:roles` | Create custom role |
| `/organizations/{id}/roles/{roleId}` | PATCH | Bearer | `members:roles` | Update role |
| `/organizations/{id}/roles/{roleId}` | DELETE | Bearer | `members:roles` | Delete role |
| `/organizations/{id}/roles/{roleId}/assign` | POST | Bearer | `members:roles` | Assign role |
| `/organizations/{id}/roles/{roleId}/revoke` | POST | Bearer | `members:roles` | Revoke role |
| `/organizations/{id}/roles/{roleId}/members` | GET | Bearer | `members:read` | List role members |
| `/organizations/{id}/roles/search` | GET | Bearer | `members:read` | Search roles |

### Self-Service (Me)

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/me` | GET | Bearer | Get current user profile |
| `/me` | PATCH | Bearer | Update profile |
| `/me` | DELETE | Bearer | Request account deletion |
| `/me/cancel-deletion` | POST | Bearer | Cancel deletion request |
| `/me/organizations` | GET | Bearer | List user's organizations |
| `/me/linked-accounts` | GET | Bearer | List linked OAuth accounts |
| `/me/permissions` | GET | Bearer | Get current permissions |
| `/me/invitations` | GET | Bearer | List pending invitations |

#### GET /me

Get current user profile.

**Response:**

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "email": "user@example.com",
  "displayName": "John Doe",
  "emailVerified": true,
  "preferredOrganizationId": "660e8400-e29b-41d4-a716-446655440000",
  "hasPasskeysRegistered": false,
  "createdAt": "2024-01-15T10:30:00Z",
  "lastAuthenticatedAt": "2024-01-20T14:45:00Z",
  "authProviders": [
    { "provider": "google", "displayName": "Google" },
    { "provider": "github", "displayName": "GitHub" }
  ]
}
```

### Sessions

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/me/sessions` | GET | Bearer | List active sessions |
| `/me/sessions/{sessionId}` | DELETE | Bearer | Revoke session |
| `/me/sessions/revoke-all` | POST | Bearer | Revoke all sessions |

### Activity

| Endpoint | Method | Auth | Permission | Description |
|----------|--------|------|------------|-------------|
| `/me/activity` | GET | Bearer | - | User's activity log |
| `/organizations/{id}/activity` | GET | Bearer | `org:audit` | Organization activity log |

### OAuth

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/oauth/{provider}/link` | GET | Bearer | Begin OAuth account linking |

### Internal (Service-to-Service)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/internal/users/{userId}` | GET | Get user info |
| `/internal/users/batch` | POST | Get multiple users |
| `/internal/organizations/{id}` | GET | Get organization info |
| `/internal/organizations/{id}/exists` | GET | Check organization exists |
| `/internal/organizations/{id}/members` | GET | Get organization members |
| `/internal/permissions/check` | POST | Check user permission |
| `/internal/users/{userId}/organizations/{orgId}/permissions` | GET | Get user permissions |
| `/internal/users/{userId}/organizations/{orgId}/membership` | GET | Get membership info |
| `/internal/assertions/microsoft` | POST | Generate WIF assertion |

---

## OAuth Providers

### Supported Gaming Platforms

| Provider | Auth Type | Configuration Keys |
|----------|-----------|-------------------|
| Steam | OpenID | `OAuth:Steam:ApplicationKey` |
| Battle.net | OAuth2 | `OAuth:BattleNet:ClientId`, `ClientSecret`, `Region` |
| Epic Games | OAuth2 | `OAuth:Epic:ClientId`, `ClientSecret`, `AuthorizationEndpoint`, `TokenEndpoint`, `UserInformationEndpoint` |
| Xbox (Microsoft) | OAuth2 | `OAuth:Xbox:ClientId`, `ClientSecret` |

### OAuth Linking Flow

Gaming platform accounts are linked via OAuth, allowing users to associate Steam IDs, Battle.net accounts, etc. with their Meridian Console profile.

```
┌────────┐     ┌──────────────┐     ┌─────────────┐
│ User   │     │   Identity   │     │   Provider  │
└───┬────┘     └──────┬───────┘     └──────┬──────┘
    │                 │                    │
    │  GET /oauth/steam/link              │
    │────────────────>│                    │
    │                 │                    │
    │  Redirect to Steam                   │
    │<────────────────────────────────────>│
    │                 │                    │
    │  Callback with auth code             │
    │─────────────────────────────────────>│
    │                 │                    │
    │                 │  Store LinkedAccount
    │                 │                    │
    │  Redirect to returnUrl              │
    │<────────────────│                    │
```

### Provider Configuration

**Steam:**

```json
{
  "OAuth": {
    "Steam": {
      "ApplicationKey": "your-steam-web-api-key"
    }
  }
}
```

**Battle.net:**

```json
{
  "OAuth": {
    "BattleNet": {
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "Region": "America"
    }
  }
}
```

Regions: `America`, `Europe`, `Korea`, `Taiwan`, `China`

**Epic Games:**

```json
{
  "OAuth": {
    "Epic": {
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "AuthorizationEndpoint": "https://www.epicgames.com/id/authorize",
      "TokenEndpoint": "https://api.epicgames.dev/epic/oauth/v1/token",
      "UserInformationEndpoint": "https://api.epicgames.dev/epic/oauth/v1/userInfo"
    }
  }
}
```

### Security Features

- PKCE (Proof Key for Code Exchange) enabled for all OAuth2 providers
- State parameter with 10-minute expiration
- Redirect URL validation against allowlist
- Provider secrets stored in Azure Key Vault (production)

---

## Webhooks

### Better Auth Webhook

The Identity service receives webhook events from Better Auth for user lifecycle synchronization.

**Endpoint:** `POST /webhooks/better-auth`

### Supported Events

| Event | Action |
|-------|--------|
| `user.deleted` | Soft-delete user, deactivate memberships |
| `user.updated` | Sync email, email verification status |
| `passkey.registered` | Update passkey registration flag |

### Webhook Signature Validation

Webhooks use HMAC-SHA256 signature validation:

1. Better Auth sends signature in `X-Webhook-Signature` header
2. Format: `t=timestamp,v1=signature`
3. Signature: HMAC-SHA256 of `timestamp.body` with shared secret
4. Timestamp must be within 5 minutes (configurable)

### Configuration

```json
{
  "Webhooks": {
    "BetterAuthSecretName": "better-auth-webhook-secret",
    "SignatureHeader": "X-Webhook-Signature",
    "MaxTimestampAgeSeconds": 300,
    "SecretCacheMinutes": 60
  }
}
```

### Example Webhook Payload

```json
{
  "event": "user.updated",
  "data": {
    "externalAuthId": "ba_123456",
    "email": "user@example.com",
    "emailVerified": true
  }
}
```

---

## Event Publishing

### MassTransit Integration

The Identity service publishes events via MassTransit/RabbitMQ for other services to consume.

### Published Events

#### UserAuthenticated

Published after successful token exchange.

```csharp
public record UserAuthenticated(
    Guid UserId,
    Guid OrganizationId,
    string ExternalAuthId,
    string Email,
    string? ClientApp,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset AuthenticatedAt);
```

#### OrgMembershipChanged

Published when membership status changes.

```csharp
public record OrgMembershipChanged(
    Guid UserId,
    Guid OrganizationId,
    string ChangeType,  // "joined", "left", "role_changed"
    string? Role,
    DateTimeOffset ChangedAt);
```

#### UserDeactivated

Published when a user account is deactivated.

```csharp
public record UserDeactivated(
    Guid UserId,
    string ExternalAuthId,
    string Reason,  // "user.deleted", "admin_action"
    DateTimeOffset DeactivatedAt);
```

### Configuration

```json
{
  "RabbitMq": {
    "Host": "localhost",
    "Username": "dhadgar",
    "Password": "your-password"
  }
}
```

---

## Multi-Tenancy

### Organization Model

- Each user can belong to multiple organizations
- Users have different roles in each organization
- Tokens are scoped to a single organization context

### Organization Switching

When a user switches organizations, new tokens are issued with updated permissions:

```http
POST /organizations/{newOrgId}/switch
Authorization: Bearer <current-token>
```

Response includes new tokens with the target organization's permissions.

### Preferred Organization

Users can set a preferred organization that becomes the default context:

```http
PATCH /me
Content-Type: application/json

{
  "preferredOrganizationId": "770e8400-e29b-41d4-a716-446655440000"
}
```

### Token Organization Context

The `org_id` claim in JWT indicates the current organization context. All authorization checks consider this claim.

---

## Audit Logging

### Event Types

The `AuditEventTypes` class defines standard event types:

```csharp
// User events
public const string UserCreated = "user.created";
public const string UserUpdated = "user.updated";
public const string UserDeleted = "user.deleted";
public const string UserDeletionRequested = "user.deletion_requested";
public const string UserAuthenticated = "user.authenticated";
public const string UserAuthenticationFailed = "user.authentication_failed";

// Organization events
public const string OrganizationCreated = "organization.created";
public const string OrganizationUpdated = "organization.updated";
public const string OrganizationDeleted = "organization.deleted";
public const string OrganizationOwnershipTransferred = "organization.ownership_transferred";

// Membership events
public const string MembershipInvited = "membership.invited";
public const string MembershipAccepted = "membership.accepted";
public const string MembershipRejected = "membership.rejected";
public const string MembershipWithdrawn = "membership.withdrawn";
public const string MembershipRemoved = "membership.removed";
public const string MembershipExpired = "membership.expired";

// Role events
public const string RoleCreated = "role.created";
public const string RoleUpdated = "role.updated";
public const string RoleDeleted = "role.deleted";
public const string RoleAssigned = "role.assigned";
public const string RoleRevoked = "role.revoked";

// Claim events
public const string ClaimGranted = "claim.granted";
public const string ClaimRevoked = "claim.revoked";

// OAuth events
public const string OAuthAccountLinked = "oauth.account_linked";
public const string OAuthAccountUnlinked = "oauth.account_unlinked";

// Token events
public const string TokenIssued = "token.issued";
public const string TokenRefreshed = "token.refreshed";
public const string TokenRevoked = "token.revoked";
public const string SessionRevoked = "session.revoked";
public const string AllSessionsRevoked = "session.all_revoked";

// Security events
public const string PrivilegeEscalationAttempt = "security.privilege_escalation_attempt";
public const string AuthorizationDenied = "security.authorization_denied";
public const string SuspiciousActivity = "security.suspicious_activity";
public const string RateLimitExceeded = "security.rate_limit_exceeded";
```

### Querying Audit Logs

**User activity:**

```http
GET /me/activity?take=50&skip=0
Authorization: Bearer <token>
```

**Organization activity:**

```http
GET /organizations/{orgId}/activity?take=50&skip=0&eventType=membership.invited
Authorization: Bearer <token>
```

### Retention

Audit events are stored indefinitely by default. The `AuditService` provides methods for retention management:

```csharp
await auditService.DeleteEventsBeforeAsync(DateTime.UtcNow.AddDays(-365));
```

---

## Testing

### Test Project Structure

```
tests/Dhadgar.Identity.Tests/
├── HelloWorldTests.cs                    # Basic endpoint tests
├── SwaggerTests.cs                       # OpenAPI spec validation
├── IdentityModelTests.cs                 # Entity model tests
├── IdentityWebApplicationFactory.cs      # Test fixture
├── TestIdentityEventPublisher.cs         # Mock event publisher
├── TokenExchangeServiceTests.cs          # Token exchange logic
├── PermissionServiceTests.cs             # Permission calculation
├── OrganizationServiceTests.cs           # Organization CRUD
├── OrganizationSwitchServiceTests.cs     # Org switching
├── OrganizationOwnershipTests.cs         # Ownership transfer
├── MembershipServiceTests.cs             # Membership operations
├── InvitationWorkflowTests.cs            # Invitation flow
├── RoleServiceTests.cs                   # Role management
├── RefreshTokenServiceTests.cs           # Token refresh
├── LinkedAccountServiceTests.cs          # OAuth linking
├── AuditServiceTests.cs                  # Audit logging
├── BulkOperationTests.cs                 # Bulk operations
├── WebhookEndpointTests.cs               # Webhook handling
├── UserSelfDeletionTests.cs              # Account deletion
├── Integration/
│   ├── AuthenticationFlowIntegrationTests.cs
│   ├── ClientCredentialsFlowIntegrationTests.cs
│   ├── OrganizationSwitchIntegrationTests.cs
│   ├── OAuthProviderIntegrationTests.cs
│   ├── SecurityIntegrationTests.cs
│   ├── EndpointErrorPathTests.cs
│   ├── ReadinessIntegrationTests.cs
│   ├── ActivityEndpointTests.cs
│   └── BulkEndpointTests.cs
└── OAuth/
    └── OAuthLinkingTests.cs
```

### Running Tests

```bash
# Run all tests
dotnet test tests/Dhadgar.Identity.Tests

# Run specific test class
dotnet test tests/Dhadgar.Identity.Tests --filter "FullyQualifiedName~TokenExchangeServiceTests"

# Run specific test
dotnet test tests/Dhadgar.Identity.Tests --filter "FullyQualifiedName~TokenExchangeServiceTests.ExchangeAsync_ValidToken_ReturnsTokens"

# Run with verbose output
dotnet test tests/Dhadgar.Identity.Tests -v detailed
```

### Test Infrastructure

The `IdentityWebApplicationFactory` provides:

- In-memory database for isolation
- Mock OAuth providers
- Test authentication handler
- Mock event publisher
- Mock webhook secret provider

**Example test setup:**

```csharp
public class MyTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public MyTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MyTest()
    {
        // Seed test data
        var userId = await _factory.SeedUserAsync("test@example.com");

        // Create authenticated client
        var client = _factory.CreateAuthenticatedClient(userId, orgId, "admin");

        // Make request
        var response = await client.GetAsync("/me");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

### Test Authentication

Tests use header-based authentication instead of JWT:

```csharp
client.DefaultRequestHeaders.Add("X-Test-User-Id", userId.ToString());
client.DefaultRequestHeaders.Add("X-Test-Org-Id", orgId.ToString());
client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
```

---

## Troubleshooting

### Common Issues

#### 1. "Redis connection string is required"

**Cause:** Redis is not configured.

**Solution:**

```bash
# Option 1: Start Redis via Docker Compose
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# Option 2: Configure connection string
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379" --project src/Dhadgar.Identity
```

#### 2. "Key Vault certificate configuration is required"

**Cause:** Running in production mode without Key Vault.

**Solution:**

```bash
# For development, enable development certificates
dotnet user-secrets set "Auth:UseDevelopmentCertificates" "true" --project src/Dhadgar.Identity

# For production, configure Key Vault
dotnet user-secrets set "Auth:KeyVault:VaultUri" "https://your-vault.vault.azure.net/" --project src/Dhadgar.Identity
```

#### 3. Token exchange returns "invalid_exchange_token"

**Causes:**

- Exchange token expired (5 minute lifetime)
- Wrong signing key configured
- Token not issued by Better Auth

**Debug steps:**

1. Check `Auth:Exchange:PublicKeyPem` matches Better Auth's private key
2. Verify token hasn't expired (check `exp` claim)
3. Check issuer matches `Auth:Exchange:Issuer`

#### 4. "token_already_used" error

**Cause:** Exchange token replay protection triggered.

**Solution:** Exchange tokens are single-use. Request a new token from Better Auth.

#### 5. Migrations fail with "connection refused"

**Cause:** PostgreSQL not running.

**Solution:**

```bash
# Start PostgreSQL
docker compose -f deploy/compose/docker-compose.dev.yml up -d postgres

# Verify connection
psql -h localhost -U dhadgar -d dhadgar_platform
```

#### 6. OAuth linking redirects to wrong URL

**Cause:** Return URL not in allowlist.

**Solution:** Add the URL to configuration:

```json
{
  "OAuth": {
    "AllowedRedirectHosts": ["localhost", "your-domain.com"]
  }
}
```

### Logging

Enable debug logging for detailed output:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Dhadgar.Identity": "Debug",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### Database Inspection

```sql
-- Check user by email
SELECT * FROM "AspNetUsers" WHERE "NormalizedEmail" = 'USER@EXAMPLE.COM';

-- Check memberships
SELECT uo.*, o."Name" AS "OrgName"
FROM "UserOrganizations" uo
JOIN "Organizations" o ON uo."OrganizationId" = o."Id"
WHERE uo."UserId" = 'your-user-id';

-- Check recent audit events
SELECT * FROM "AuditEvents"
ORDER BY "OccurredAtUtc" DESC
LIMIT 20;
```

---

## Security Considerations

### Authentication Security

1. **Short-lived access tokens**: 15 minutes default
2. **Refresh token rotation**: New refresh token on each use
3. **Token replay protection**: Redis-based JTI tracking
4. **Constant-time comparison**: For signature validation

### Rate Limiting

| Endpoint | Limit | Window |
|----------|-------|--------|
| `/exchange` | 5 requests | 1 minute |
| `/connect/token` | 20 requests | 1 minute |
| OAuth endpoints | 60 requests | 1 minute |
| Webhooks | 30 requests | 1 minute |
| Invitations | 10 requests | 1 hour |
| Default (authenticated) | 100 requests | 1 minute |
| Default (unauthenticated) | 30 requests | 1 minute |

### Request Size Limits

- Max request body: 1 MB
- Max headers: 32 KB
- Max request line: 8 KB

### Best Practices

1. **Never commit secrets**: Use user-secrets for development, Key Vault for production
2. **Use HTTPS**: TLS required in production (OpenIddict enforces this)
3. **Validate redirect URLs**: Only allow registered domains
4. **Enable PKCE**: Required for all OAuth2 flows
5. **Monitor audit logs**: Review for suspicious patterns
6. **Rotate secrets regularly**: Especially webhook secrets and client secrets

### Security Events to Monitor

- `security.privilege_escalation_attempt`: User tried to grant themselves higher permissions
- `security.authorization_denied`: Authorization failure
- `security.suspicious_activity`: Potential attack detected
- `security.rate_limit_exceeded`: Rate limit triggered

---

## Related Documentation

- [Identity API Reference](../../docs/identity-api-reference.md)
- [Identity Claims Reference](../../docs/identity-claims-reference.md)
- [Identity Webhooks](../../docs/identity-webhooks.md)
- [OAuth Provider Setup](../../docs/identity-oauth-providers.md)
- [Implementation Plan](../../docs/implementation-plans/identity-service.md)
- [Deployment Runbook](../../docs/runbooks/identity-service-deployment.md)
- [CLAUDE.md](../../CLAUDE.md) - Project-wide development guidelines
