# Identity Service Deployment Runbook

**Service**: Dhadgar.Identity
**Port**: 5010
**Version**: 1.0
**Last Updated**: 2026-01-13

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Azure Key Vault Setup](#azure-key-vault-setup)
3. [OpenIddict Client Seeding](#openiddict-client-seeding)
4. [Redis Configuration](#redis-configuration)
5. [Database Migrations](#database-migrations)
6. [Certificate Rotation](#certificate-rotation)
7. [Troubleshooting](#troubleshooting)
8. [Monitoring & Alerts](#monitoring--alerts)

---

## Prerequisites

### Required Infrastructure

- **PostgreSQL 16+**: Identity database
- **Redis 7+**: Exchange token replay protection, refresh token storage
- **RabbitMQ 3.x**: Event publishing (MassTransit)
- **Azure Key Vault**: Certificate storage (production only)

### Required Secrets

| Secret | Purpose | Location |
|--------|---------|----------|
| `ConnectionStrings:Postgres` | Database connection | User Secrets (dev) / Azure Key Vault (prod) |
| `Redis:ConnectionString` | Redis connection | User Secrets (dev) / Azure Key Vault (prod) |
| `RabbitMq:Host`, `RabbitMq:Username`, `RabbitMq:Password` | Message bus | User Secrets (dev) / Azure Key Vault (prod) |
| `Auth:KeyVault:VaultUri` | Key Vault URI | appsettings.Production.json |
| `Auth:KeyVault:SigningCertName` | Signing cert name in Key Vault | appsettings.Production.json |
| `Auth:KeyVault:EncryptionCertName` | Encryption cert name in Key Vault | appsettings.Production.json |
| `Auth:Exchange:PublicKeyPem` | Better Auth ES256 public key | User Secrets (dev) / Azure Key Vault (prod) |
| `Webhooks:BetterAuth:Secret` | Better Auth webhook HMAC secret | User Secrets (dev) / Azure Key Vault (prod) |

### Required Azure RBAC Roles

| Resource | Role | Purpose |
|----------|------|---------|
| Azure Key Vault | **Key Vault Certificates User** | Read signing/encryption certificates |
| Azure Key Vault | **Key Vault Secrets User** | Read certificate private keys (exportable certs) |

---

## Azure Key Vault Setup

### 1. Create Signing and Encryption Certificates

The Identity service requires two certificates for OpenIddict:
- **Signing Certificate**: Signs JWTs (RSA 2048 or ECDSA P-256)
- **Encryption Certificate**: Encrypts access tokens (RSA 2048)

#### Option A: Generate Certificates in Key Vault (Recommended)

```bash
# Variables
VAULT_NAME="meridian-identity-kv"
SIGNING_CERT_NAME="oidc-signing-cert"
ENCRYPTION_CERT_NAME="oidc-encryption-cert"

# Create signing certificate (ECDSA P-256 for smaller JWTs)
az keyvault certificate create \
  --vault-name $VAULT_NAME \
  --name $SIGNING_CERT_NAME \
  --policy '{
    "keyProperties": {
      "exportable": true,
      "keyType": "EC",
      "keyCurveName": "P-256",
      "reuseKey": false
    },
    "secretProperties": {
      "contentType": "application/x-pkcs12"
    },
    "issuerParameters": {
      "name": "Self"
    },
    "x509CertificateProperties": {
      "subject": "CN=Meridian Identity Signing",
      "validityInMonths": 12
    }
  }'

# Create encryption certificate (RSA 2048)
az keyvault certificate create \
  --vault-name $VAULT_NAME \
  --name $ENCRYPTION_CERT_NAME \
  --policy '{
    "keyProperties": {
      "exportable": true,
      "keyType": "RSA",
      "keySize": 2048,
      "reuseKey": false
    },
    "secretProperties": {
      "contentType": "application/x-pkcs12"
    },
    "issuerParameters": {
      "name": "Self"
    },
    "x509CertificateProperties": {
      "subject": "CN=Meridian Identity Encryption",
      "validityInMonths": 12
    }
  }'
```

#### Option B: Import Existing PFX Certificates

```bash
# Import existing certificates
az keyvault certificate import \
  --vault-name $VAULT_NAME \
  --name $SIGNING_CERT_NAME \
  --file signing-cert.pfx \
  --password "pfx-password"

az keyvault certificate import \
  --vault-name $VAULT_NAME \
  --name $ENCRYPTION_CERT_NAME \
  --file encryption-cert.pfx \
  --password "pfx-password"
```

### 2. Grant Identity Service Access

```bash
# Get the Managed Identity Object ID of the Identity service
# (from Azure App Service or AKS pod identity)
IDENTITY_OBJECT_ID="<managed-identity-object-id>"

# Grant Key Vault access
az keyvault set-policy \
  --name $VAULT_NAME \
  --object-id $IDENTITY_OBJECT_ID \
  --certificate-permissions get list \
  --secret-permissions get list
```

### 3. Configure appsettings.Production.json

```json
{
  "Auth": {
    "Issuer": "https://identity.meridianconsole.com",
    "Audience": "meridian-api",
    "KeyVault": {
      "VaultUri": "https://meridian-identity-kv.vault.azure.net/",
      "SigningCertName": "oidc-signing-cert",
      "EncryptionCertName": "oidc-encryption-cert"
    },
    "UseDevelopmentCertificates": false
  }
}
```

---

## OpenIddict Client Seeding

### Development Client (Auto-Seeded)

The service automatically seeds a `dev-client` in Development mode for testing. This client is **NOT** created in production.

### Production Clients

Production clients must be created manually via database seeding script or admin API.

#### Example: Seed Gateway Service Client

```sql
-- Insert client for Gateway service
INSERT INTO "OpenIddictApplications" (
    "Id",
    "ClientId",
    "ClientSecret", -- Hashed by OpenIddict
    "DisplayName",
    "Permissions",
    "Type"
)
VALUES (
    gen_random_uuid(),
    'gateway-service',
    '<hashed-secret>', -- Use OpenIddict's IOpenIddictApplicationManager.CreateAsync for proper hashing
    'Gateway Service',
    '["ept:token","ept:introspection","gt:client_credentials","scp:servers:read","scp:servers:write","scp:nodes:manage"]',
    'confidential'
);
```

**Recommended**: Use Entity Framework or OpenIddict's `IOpenIddictApplicationManager` API for seeding to ensure proper secret hashing:

```csharp
// Seeding script using OpenIddict
var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

var descriptor = new OpenIddictApplicationDescriptor
{
    ClientId = "gateway-service",
    ClientSecret = "<strong-random-secret>",
    DisplayName = "Gateway Service",
    Permissions =
    {
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.Endpoints.Introspection,
        OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
        OpenIddictConstants.Permissions.Prefixes.Scope + "servers:read",
        OpenIddictConstants.Permissions.Prefixes.Scope + "servers:write",
        OpenIddictConstants.Permissions.Prefixes.Scope + "nodes:manage"
    }
};

await manager.CreateAsync(descriptor);
```

---

## Redis Configuration

### Connection String Format

```
host:port,password=yourpassword,ssl=true,abortConnect=false
```

### Required Settings

| Setting | Purpose | Value |
|---------|---------|-------|
| `ssl=true` | Encrypted connection | Production only |
| `abortConnect=false` | Graceful degradation on connection failures | All environments |
| `connectRetry=3` | Retry connection attempts | Production |

### Azure Redis Cache (Recommended)

```bash
# Create Azure Redis Cache
az redis create \
  --name meridian-redis \
  --resource-group meridian-rg \
  --location centralus \
  --sku Basic \
  --vm-size c0

# Get connection string
az redis list-keys \
  --name meridian-redis \
  --resource-group meridian-rg \
  --query primaryKey -o tsv
```

### Redis Backup Strategy

**Exchange Token Replay Store:**
- **Data**: Single-use token JTIs (60-second TTL)
- **Criticality**: Medium (replay attacks possible if lost, but short window)
- **Backup**: Not required (ephemeral data with 60s expiry)

**Refresh Tokens:**
- **Data**: Hashed refresh tokens (7-day TTL)
- **Criticality**: High (users need to re-authenticate if lost)
- **Backup**: Snapshot every 6 hours, retain for 7 days

```bash
# Azure Redis: Enable RDB persistence
az redis patch \
  --name meridian-redis \
  --resource-group meridian-rg \
  --set redisConfiguration.rdb-backup-enabled=true \
  --set redisConfiguration.rdb-backup-frequency=360 \
  --set redisConfiguration.rdb-storage-connection-string="<storage-connection-string>"
```

---

## Database Migrations

### Auto-Migration (Development Only)

In Development mode, migrations apply automatically on startup:

```json
{
  "Database": {
    "AutoMigrate": true
  }
}
```

### Production Migration Strategy

**IMPORTANT**: Never use `AutoMigrate=true` in production. Migrations must be applied manually with downtime coordination.

#### Step 1: Generate Migration Script

```bash
# Generate SQL script for review
dotnet ef migrations script \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity \
  --idempotent \
  --output migration-$(date +%Y%m%d).sql
```

#### Step 2: Review Migration

- **Check for breaking changes** (column drops, renames)
- **Verify indexes** (large tables may cause downtime)
- **Test in staging environment** first

#### Step 3: Apply Migration with Downtime Window

```bash
# Option A: Apply via kubectl (Kubernetes)
kubectl exec -it deployment/identity-service -- \
  dotnet ef database update \
  --project /app/Dhadgar.Identity.dll \
  --no-build

# Option B: Apply via psql (direct database access)
psql -h postgres-host -U dhadgar -d dhadgar_identity -f migration-20260113.sql
```

#### Step 4: Verify Migration

```sql
-- Check migration history
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 5;

-- Verify new tables/columns
\d+ "Users"
\d+ "Organizations"
```

### Rollback Strategy

EF Core does not support automatic rollbacks. To rollback:

1. **Downtime required** (stop Identity service)
2. **Apply reverse migration:**

```bash
dotnet ef database update PreviousMigrationName \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity
```

3. **Deploy previous service version**
4. **Verify database state**

---

## Certificate Rotation

### When to Rotate

- **Signing Certificate**: Every 12 months (before expiry)
- **Encryption Certificate**: Every 12 months (before expiry)
- **Compromised**: Immediately if compromised

### Rotation Procedure

#### Step 1: Create New Certificates

```bash
# Generate new certificates with updated expiry
az keyvault certificate create \
  --vault-name $VAULT_NAME \
  --name oidc-signing-cert-2026 \
  --policy '{...}' # Same policy as original
```

#### Step 2: Add New Certificate to Service (Zero-Downtime)

OpenIddict supports multiple signing keys during rotation. Update configuration to use both old and new keys:

```csharp
// In Program.cs
options.AddSigningKey(oldSigningKey)
       .AddSigningKey(newSigningKey) // New key will be used for new tokens
       .AddEncryptionCertificate(oldEncryptionCert)
       .AddEncryptionCertificate(newEncryptionCert);
```

#### Step 3: Deploy Service with Both Keys

- New tokens signed with **new key**
- Old tokens validated with **old key** (until expiry)

#### Step 4: Wait for Old Tokens to Expire

- Wait for **maximum token lifetime** (7 days for refresh tokens, 15 minutes for access tokens)

#### Step 5: Remove Old Certificate

Update configuration to use only new keys and redeploy.

#### Step 6: Revoke Old Certificate in Key Vault

```bash
az keyvault certificate set-attributes \
  --vault-name $VAULT_NAME \
  --name oidc-signing-cert-old \
  --enabled false
```

---

## Troubleshooting

### Issue: "Failed to load OpenIddict certificates from Key Vault"

**Symptoms:**
```
InvalidOperationException: Failed to load OpenIddict certificates from Key Vault (https://...). Ensure you are logged in via 'az login'...
```

**Causes:**
1. Service Managed Identity missing Key Vault access
2. Certificate not marked exportable
3. Certificate missing private key

**Resolution:**

```bash
# 1. Verify Managed Identity has access
az keyvault show --name $VAULT_NAME --query properties.accessPolicies

# 2. Check if certificate is exportable
az keyvault certificate show \
  --vault-name $VAULT_NAME \
  --name $SIGNING_CERT_NAME \
  --query policy.keyProperties.exportable

# 3. If not exportable, recreate with exportable flag
az keyvault certificate create \
  --vault-name $VAULT_NAME \
  --name $SIGNING_CERT_NAME \
  --policy '{"keyProperties": {"exportable": true, ...}}'
```

### Issue: "Redis connection failed: No connection is available"

**Symptoms:**
```
StackExchange.Redis.RedisConnectionException: No connection is available to service this operation
```

**Causes:**
1. Redis connection string incorrect
2. Redis TLS/SSL mismatch
3. Firewall blocking connection

**Resolution:**

```bash
# Test Redis connection
redis-cli -h <redis-host> -p <redis-port> -a <password> PING

# Check Azure Redis firewall rules
az redis firewall-rules list \
  --name meridian-redis \
  --resource-group meridian-rg

# Add Identity service IP to firewall
az redis firewall-rules create \
  --name meridian-redis \
  --resource-group meridian-rg \
  --rule-name identity-service \
  --start-ip <service-ip> \
  --end-ip <service-ip>
```

### Issue: "Exchange token replay attack detected"

**Symptoms:**
```
HTTP 401: Exchange token has already been used
```

**Causes:**
1. Client retrying failed exchange requests
2. Redis key expiry too short
3. Clock skew between services

**Resolution:**

1. **Verify Redis TTL** (should be 120 seconds minimum):

```csharp
// In RedisExchangeTokenReplayStore.cs
await db.StringSetAsync(key, "1", expiry: TimeSpan.FromSeconds(120));
```

2. **Check clock skew:**

```bash
# Verify NTP sync on all servers
timedatectl status

# If skewed, sync clocks
sudo ntpdate -s time.nist.gov
```

### Issue: "Database migration pending"

**Symptoms:**
```
Microsoft.EntityFrameworkCore.Infrastructure[10407]: Pending model changes detected
```

**Causes:**
1. AutoMigrate disabled in production
2. Migration not applied

**Resolution:**

```bash
# Apply pending migrations
dotnet ef database update \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity
```

---

## Monitoring & Alerts

### Key Metrics

| Metric | Threshold | Alert |
|--------|-----------|-------|
| Token exchange latency | >500ms (p95) | Warning |
| Token exchange latency | >1000ms (p95) | Critical |
| Token exchange errors | >5% | Critical |
| Redis connection errors | >1% | Warning |
| Redis unavailable | >30s | Critical |
| Database query latency | >200ms (p95) | Warning |
| JWT signature validation errors | >1% | Critical (possible key rotation issue) |
| Certificate expiry | <30 days | Warning |
| Certificate expiry | <7 days | Critical |

### Recommended Dashboards

**Grafana Dashboard: Identity Service Overview**

Panels:
1. Token exchange rate (req/sec)
2. Token exchange latency (p50, p95, p99)
3. OAuth provider usage (pie chart)
4. Organization creation rate
5. Active users (distinct users authenticated in last 24h)
6. Redis hit rate
7. Database connection pool usage
8. Error rate by endpoint

### Log Queries

**New Relic NRQL Examples:**

```sql
-- Exchange token failures
SELECT count(*)
FROM Log
WHERE service.name = 'Dhadgar.Identity'
  AND message LIKE '%token exchange failed%'
FACET error_type
SINCE 1 hour ago

-- Slow token exchanges
SELECT percentile(duration, 95)
FROM Span
WHERE service.name = 'Dhadgar.Identity'
  AND name = 'POST /exchange'
SINCE 1 hour ago
TIMESERIES

-- Redis errors
SELECT count(*)
FROM Log
WHERE service.name = 'Dhadgar.Identity'
  AND message LIKE '%Redis%'
  AND level = 'Error'
SINCE 1 hour ago
```

---

## Emergency Procedures

### Complete Service Outage

1. **Check dependencies:**
   - PostgreSQL: `psql -h <host> -U dhadgar -c "SELECT 1"`
   - Redis: `redis-cli -h <host> PING`
   - RabbitMQ: `curl http://<host>:15672/api/overview`

2. **Check service logs:**
   ```bash
   kubectl logs -f deployment/identity-service --tail=100
   ```

3. **Rollback if recent deployment:**
   ```bash
   kubectl rollout undo deployment/identity-service
   ```

### Certificate Compromised

1. **Immediately revoke compromised certificate:**
   ```bash
   az keyvault certificate set-attributes \
     --vault-name $VAULT_NAME \
     --name oidc-signing-cert \
     --enabled false
   ```

2. **Generate new certificate** (see Certificate Rotation)

3. **Deploy service with new certificate**

4. **Invalidate all existing tokens:**
   - Flush Redis: `redis-cli FLUSHDB`
   - Notify users of forced re-authentication

### Redis Data Loss

1. **Refresh tokens lost** → Users must re-authenticate
   - Notify users via email/in-app message
   - Monitor support requests

2. **Exchange token replay store lost** → Short-term vulnerability window (60 seconds)
   - Monitor for unusual token exchange patterns
   - Consider temporary rate limiting

---

## Deployment Checklist

### Pre-Deployment

- [ ] Certificates created in Key Vault
- [ ] Managed Identity has Key Vault access (Certificates User, Secrets User)
- [ ] Redis cache provisioned and accessible
- [ ] PostgreSQL database created
- [ ] RabbitMQ vhost and user configured
- [ ] Secrets configured in Azure Key Vault or user secrets
- [ ] Migration script generated and reviewed
- [ ] Staging environment tested

### Deployment

- [ ] Apply database migrations
- [ ] Deploy service with new image tag
- [ ] Verify health endpoint: `GET /healthz`
- [ ] Verify JWKS endpoint: `GET /.well-known/jwks.json`
- [ ] Test token exchange flow (end-to-end)
- [ ] Test client credentials flow (service-to-service)
- [ ] Verify OpenTelemetry traces in Grafana/New Relic

### Post-Deployment

- [ ] Monitor error rates for 30 minutes
- [ ] Check certificate expiry dates
- [ ] Verify Redis backup enabled
- [ ] Document deployment version and timestamp
- [ ] Update runbook with any new findings

---

## Contact & Escalation

| Issue | Contact |
|-------|---------|
| Certificate issues | DevOps team |
| Database performance | Database admin team |
| Redis issues | Infrastructure team |
| OAuth provider issues | External provider support + DevOps |
| Security incidents | Security team (immediate escalation) |

---

**End of Runbook**
