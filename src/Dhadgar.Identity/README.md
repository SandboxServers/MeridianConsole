# Dhadgar.Identity

Identity and access management service for Meridian Console. Provides authentication, authorization, and user/organization management.

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

# Or with watch mode
dotnet watch --project src/Dhadgar.Identity
```

The service runs on `http://localhost:5000` by default.

### Swagger UI

Access the API documentation at: `http://localhost:5000/swagger`

## Configuration

### Required Configuration

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Postgres` | PostgreSQL connection string | localhost:5432 |
| `Redis:ConnectionString` | Redis connection string | localhost:6379 |
| `Auth:Issuer` | JWT issuer URL | - |
| `Auth:Audience` | JWT audience | meridian-api |

### Authentication Configuration

```json
{
  "Auth": {
    "Issuer": "https://meridianconsole.com/api/v1/identity",
    "Audience": "meridian-api",
    "AccessTokenLifetimeSeconds": 900,
    "RefreshTokenLifetimeDays": 7,
    "UseDevelopmentCertificates": true,
    "KeyVault": {
      "VaultUri": "https://your-vault.vault.azure.net/",
      "SigningCertName": "identity-signing-cert",
      "EncryptionCertName": "identity-encryption-cert"
    }
  }
}
```

### Development Mode

Set `Auth:UseDevelopmentCertificates` to `true` to use ephemeral certificates (no Key Vault required).

## API Overview

### Authentication Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/exchange` | POST | Exchange Better Auth token for JWT |
| `/logout` | POST | Revoke current session |
| `/connect/token` | POST | OpenIddict token endpoint |

### Organization Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/organizations` | GET | List user's organizations |
| `/organizations` | POST | Create organization |
| `/organizations/{id}` | GET | Get organization details |
| `/organizations/{id}` | PATCH | Update organization |
| `/organizations/{id}` | DELETE | Soft-delete organization |
| `/organizations/{id}/switch` | POST | Switch active organization |
| `/organizations/{id}/transfer-ownership` | POST | Transfer ownership |

### Membership Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/organizations/{id}/members` | GET | List members |
| `/organizations/{id}/members/invite` | POST | Invite member |
| `/organizations/{id}/members/accept` | POST | Accept invitation |
| `/organizations/{id}/members/{memberId}` | DELETE | Remove member |
| `/organizations/{id}/members/{memberId}/role` | POST | Assign role |

### Self-Service Endpoints (`/me`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/me` | GET | Get current user profile |
| `/me` | PATCH | Update profile |
| `/me/organizations` | GET | List user's organizations |
| `/me/permissions` | GET | Get current permissions |
| `/me/sessions` | GET | List active sessions |
| `/me/sessions/{id}` | DELETE | Revoke session |
| `/me/activity` | GET | View activity log |

See [Identity API Reference](../../docs/identity-api-reference.md) for complete documentation.

## Database

### Migrations

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
```

### Auto-Migration

In Development mode or when `Database:AutoMigrate` is `true`, migrations are applied automatically on startup.

## Architecture

### Authentication Flow

1. User authenticates via Better Auth (social OAuth)
2. Better Auth issues an exchange token (short-lived, single-use)
3. Client calls `/exchange` with the exchange token
4. Identity service validates and exchanges for JWT + refresh token
5. Client uses JWT for API calls

### Authorization Model

The service uses a hybrid roles + claims authorization model:

- **Roles**: `owner`, `admin`, `operator`, `viewer`
- **Claims**: Fine-grained permissions (e.g., `servers:read`, `nodes:manage`)
- **Custom Claims**: Grant or deny specific permissions per user

See [Identity Claims Reference](../../docs/identity-claims-reference.md) for details.

### Multi-Tenancy

- Users can belong to multiple organizations
- JWT includes `org_id` claim for current organization context
- Organization switching issues new tokens with updated permissions

## Testing

```bash
# Run all tests
dotnet test tests/Dhadgar.Identity.Tests

# Run specific test
dotnet test tests/Dhadgar.Identity.Tests --filter "FullyQualifiedName~TokenExchange"
```

## Related Documentation

- [Identity API Reference](../../docs/identity-api-reference.md)
- [Identity Claims Reference](../../docs/identity-claims-reference.md)
- [Identity Webhooks](../../docs/identity-webhooks.md)
- [OAuth Provider Setup](../../docs/identity-oauth-providers.md)
- [Implementation Plan](../../docs/implementation-plans/identity-service.md)
- [Deployment Runbook](../../docs/runbooks/identity-service-deployment.md)
