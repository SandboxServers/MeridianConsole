# Secrets Service Operations Runbook

**Service**: Dhadgar.Secrets
**Port**: 5110
**Version**: 1.0
**Last Updated**: 2026-01-22

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Deployment Procedures](#deployment-procedures)
4. [Secret Rotation Workflows](#secret-rotation-workflows)
5. [Emergency Break-Glass Procedures](#emergency-break-glass-procedures)
6. [Azure Key Vault Integration](#azure-key-vault-integration)
7. [Common Issues and Solutions](#common-issues-and-solutions)
8. [Monitoring and Alerts](#monitoring-and-alerts)
9. [Security Considerations](#security-considerations)

---

## Overview

The Secrets service provides centralized management of secrets, certificates, and Key Vault resources for the Meridian Console platform. It acts as a secure intermediary between services and Azure Key Vault, enforcing permission-based access control and comprehensive audit logging.

### Key Features

- **Secret Management**: Read, write, rotate, and delete secrets
- **Certificate Management**: List, import, and delete certificates
- **Key Vault Management**: Full CRUD operations for Azure Key Vaults
- **Claims-Based Authorization**: Permission hierarchy with category and secret-level access
- **Audit Logging**: All operations logged for security compliance
- **Rate Limiting**: Tiered limits for read, write, and rotate operations

### Architecture

```
Gateway (YARP)
    |
    v
Secrets Service (Port 5110)
    |
    +-- ISecretProvider --> KeyVaultSecretProvider (prod) / DevelopmentSecretProvider (dev)
    |
    +-- ICertificateProvider --> KeyVaultCertificateProvider
    |
    +-- IKeyVaultManager --> AzureKeyVaultManager
    |
    v
Azure Key Vault
```

---

## Prerequisites

### Required Infrastructure

| Component | Purpose | Notes |
|-----------|---------|-------|
| Azure Key Vault | Secret storage | At least one vault required |
| Azure Subscription | Vault management | Required for vault CRUD operations |
| Identity Service | JWT authentication | Issues tokens with permission claims |
| Gateway | Request routing | Routes `/api/v1/secrets/*` to Secrets service |

### Required Azure RBAC Roles

The service principal or managed identity needs the following roles:

| Role | Scope | Purpose |
|------|-------|---------|
| **Key Vault Secrets Officer** | Key Vault | Read/write/delete secrets |
| **Key Vault Certificates Officer** | Key Vault | Import/delete certificates |
| **Key Vault Contributor** | Resource Group | Create/update/delete vaults |
| **Reader** | Subscription | List vaults in subscription |

### Required Secrets Configuration

The service must have access to these configuration values:

| Configuration | Source | Purpose |
|---------------|--------|---------|
| `Secrets:KeyVaultUri` | appsettings.json / User Secrets | Default Key Vault URL |
| `Secrets:AzureSubscriptionId` | appsettings.json / Environment | For vault management |
| `Auth:Issuer` | appsettings.json | JWT issuer validation |
| `Auth:Audience` | appsettings.json | JWT audience validation |

### Workload Identity Federation (WIF) Configuration

For production deployments using federated credentials:

```json
{
  "Secrets": {
    "Wif": {
      "TenantId": "<azure-ad-tenant-id>",
      "ClientId": "<app-registration-client-id>",
      "IdentityTokenEndpoint": "http://identity:8080/connect/token"
    }
  }
}
```

---

## Deployment Procedures

### Local Development Deployment

1. **Start local infrastructure:**

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

2. **Configure user secrets:**

```bash
cd src/Dhadgar.Secrets

# Set Key Vault URI
dotnet user-secrets set "Secrets:KeyVaultUri" "https://your-vault.vault.azure.net/"

# For development mode (in-memory secrets)
dotnet user-secrets set "Secrets:UseDevelopmentProvider" "true"
```

3. **Run the service:**

```bash
dotnet run --project src/Dhadgar.Secrets
# Or with hot reload:
dotnet watch --project src/Dhadgar.Secrets
```

4. **Verify health:**

```bash
curl http://localhost:5110/healthz
curl http://localhost:5110/swagger
```

### Production Deployment

#### Pre-Deployment Checklist

- [ ] Azure Key Vault provisioned with required RBAC roles
- [ ] Managed identity or service principal configured
- [ ] Azure subscription ID configured for vault management
- [ ] Identity service deployed and issuing tokens
- [ ] Gateway routes configured for `/api/v1/secrets/*`
- [ ] Rate limiting policies reviewed for production load

#### Kubernetes Deployment

1. **Apply ConfigMap:**

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: secrets-service-config
data:
  appsettings.Production.json: |
    {
      "Secrets": {
        "KeyVaultUri": "https://mc-oauth.vault.azure.net/",
        "AzureSubscriptionId": "<subscription-id>"
      },
      "Auth": {
        "Issuer": "https://identity.meridianconsole.com",
        "Audience": "meridian-api",
        "MetadataAddress": "http://identity:8080/.well-known/openid-configuration",
        "InternalBaseUrl": "http://identity:8080"
      }
    }
```

2. **Deploy service:**

```bash
kubectl apply -f deploy/kubernetes/secrets-service.yaml
```

3. **Verify deployment:**

```bash
kubectl get pods -l app=secrets-service
kubectl logs -f deployment/secrets-service --tail=100
```

4. **Run health checks:**

```bash
kubectl exec -it deployment/secrets-service -- curl http://localhost:8080/healthz
```

#### Post-Deployment Verification

1. **Verify Gateway routing:**

```bash
curl -H "Authorization: Bearer <token>" https://api.meridianconsole.com/api/v1/secrets/healthz
```

2. **Test secret retrieval (with appropriate token):**

```bash
curl -H "Authorization: Bearer <token>" https://api.meridianconsole.com/api/v1/secrets/oauth
```

3. **Check OpenTelemetry traces in Grafana**

4. **Monitor error rates for 30 minutes**

---

## Secret Rotation Workflows

### Automatic Rotation via API

The service generates cryptographically secure random values (256-bit) when rotating secrets.

**Endpoint**: `POST /api/v1/secrets/{secretName}/rotate`

**Required Permission**: `secrets:rotate:{secretName}` or `secrets:rotate:{category}` or `secrets:*`

**Process:**

1. Generate 32 bytes of cryptographic random data
2. Base64 encode the value
3. Store as new secret version in Key Vault
4. Previous versions remain accessible by version ID
5. Cache invalidated immediately

**CLI Example:**

```bash
# Using the CLI
dhadgar secret rotate oauth-discord-client-secret

# Using curl
curl -X POST \
  -H "Authorization: Bearer <token>" \
  https://api.meridianconsole.com/api/v1/secrets/oauth-discord-client-secret/rotate
```

### Manual Rotation Procedure

For secrets that require specific values (e.g., OAuth credentials from providers):

1. **Obtain new credentials from provider** (Discord, GitHub, etc.)

2. **Update via API:**

```bash
# Using the CLI
dhadgar secret set oauth-discord-client-secret "new-secret-value"

# Using curl
curl -X PUT \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"value": "new-secret-value"}' \
  https://api.meridianconsole.com/api/v1/secrets/oauth-discord-client-secret
```

3. **Verify the update:**

```bash
dhadgar secret get oauth-discord-client-secret --reveal
```

4. **Invalidate dependent service caches** (services may cache secrets for up to 5 minutes)

5. **Test OAuth flow** with new credentials

### Scheduled Rotation Best Practices

| Secret Category | Rotation Frequency | Notes |
|-----------------|-------------------|-------|
| Infrastructure (DB passwords) | 90 days | Coordinate with DB admin |
| OAuth client secrets | 180 days | Update provider settings |
| BetterAuth secrets | 30 days | Automatic rotation supported |
| API keys | 90 days | Varies by provider |

### Rotation Audit Trail

All rotations are logged with:

- User ID of requester
- Principal type (user/service)
- New version identifier
- Timestamp
- Correlation ID

Query audit logs:

```bash
# View recent rotations (requires secrets:audit:read permission)
dhadgar secret audit --action rotate --start "7 days ago"
```

---

## Emergency Break-Glass Procedures

### When to Use Break-Glass Access

- **Security Incident**: Suspected compromise requiring immediate investigation
- **Production Outage**: Critical secret access needed to restore service
- **Lost Access**: Primary admin credentials unavailable

### Break-Glass Token Request

Break-glass tokens are issued by the Identity service with special claims:

```json
{
  "sub": "user-id",
  "break_glass": "true",
  "break_glass_reason": "INC-12345: Production outage - database password needed"
}
```

### Break-Glass Access Procedure

1. **Document the incident** in your incident management system (e.g., PagerDuty, ServiceNow)

2. **Request break-glass token** from Identity service admin:

```bash
# Admin issues break-glass token
dhadgar admin issue-break-glass-token \
  --user admin@company.com \
  --reason "INC-12345: Production outage" \
  --duration 4h
```

3. **Access required secrets:**

```bash
# All actions are logged with break_glass=true
dhadgar secret get postgres-password --reveal
```

4. **Revoke break-glass token** after incident resolution:

```bash
dhadgar admin revoke-break-glass --token-id <token-id>
```

5. **Post-incident review**: All break-glass access appears in audit logs with elevated visibility

### Break-Glass Audit Review

```bash
# Find all break-glass access events
dhadgar secret audit --break-glass-only --start "30 days ago"
```

### Break-Glass Restrictions

- Maximum token duration: **4 hours**
- All access logged at **WARNING** level
- Security team notified automatically
- Quarterly review of all break-glass usage required

---

## Azure Key Vault Integration

### Key Vault Configuration

#### Default Vault Setup

The primary vault is configured in `appsettings.json`:

```json
{
  "Secrets": {
    "KeyVaultUri": "https://mc-oauth.vault.azure.net/"
  }
}
```

#### Multiple Vaults

The service supports accessing secrets across multiple vaults via the vault management endpoints:

```bash
# List all vaults
curl -H "Authorization: Bearer <token>" \
  https://api.meridianconsole.com/api/v1/keyvaults

# Get secrets from specific vault
curl -H "Authorization: Bearer <token>" \
  https://api.meridianconsole.com/api/v1/keyvaults/my-vault/certificates
```

### Authentication Methods

#### 1. Workload Identity Federation (Recommended for Production)

```json
{
  "Secrets": {
    "Wif": {
      "TenantId": "your-tenant-id",
      "ClientId": "your-client-id",
      "IdentityTokenEndpoint": "http://identity:8080/connect/token"
    }
  }
}
```

#### 2. Managed Identity (Azure PaaS)

No configuration needed - `DefaultAzureCredential` automatically uses managed identity.

#### 3. Azure CLI (Development)

```bash
# Log in via Azure CLI
az login
az account set --subscription "<subscription-id>"

# Service uses DefaultAzureCredential which picks up CLI credentials
```

### Troubleshooting Azure Key Vault

#### Issue: "SecretClient authentication failed"

**Symptoms:**
```
Azure.Identity.AuthenticationFailedException: DefaultAzureCredential failed to retrieve a token
```

**Diagnosis:**

1. **Check credential chain:**

```bash
# Verify Azure CLI login
az account show

# Check environment variables
env | grep -i azure
```

2. **Verify RBAC permissions:**

```bash
# List role assignments on vault
az keyvault show --name mc-oauth --query properties.accessPolicies

# Or for RBAC-enabled vaults
az role assignment list --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/mc-oauth
```

3. **Test direct access:**

```bash
# Test secret retrieval directly
az keyvault secret show --vault-name mc-oauth --name oauth-discord-client-id
```

**Resolution:**

```bash
# Assign Key Vault Secrets Officer role
az role assignment create \
  --role "Key Vault Secrets Officer" \
  --assignee <service-principal-id> \
  --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/mc-oauth
```

#### Issue: "Certificate not exportable"

**Symptoms:**
```
Azure.RequestFailedException: Operation returned an invalid status code 'Forbidden'
```

**Cause:** Certificate was created without `exportable: true` flag.

**Resolution:**

```bash
# Recreate certificate with exportable flag
az keyvault certificate create \
  --vault-name mc-oauth \
  --name my-cert \
  --policy '{
    "keyProperties": {
      "exportable": true,
      "keyType": "RSA",
      "keySize": 2048
    },
    "secretProperties": {
      "contentType": "application/x-pkcs12"
    }
  }'
```

#### Issue: "Vault soft delete recovery"

**Symptoms:** Deleted vault needs to be recovered or purged.

**Recovery:**

```bash
# List deleted vaults
az keyvault list-deleted

# Recover deleted vault
az keyvault recover --name deleted-vault-name --location eastus
```

**Purge (permanent delete):**

```bash
# Purge requires purge protection to be disabled or purge permissions
az keyvault purge --name deleted-vault-name --location eastus
```

---

## Common Issues and Solutions

### Issue: "Secret not in allowed list"

**Symptoms:**
```json
HTTP 403: {"error": "Secret not in allowed list"}
```

**Cause:** Secret name not configured in `AllowedSecrets` options.

**Resolution:**

Add the secret to the appropriate category in `appsettings.json` or `SecretsOptions`:

```json
{
  "Secrets": {
    "AllowedSecrets": {
      "OAuth": [
        "oauth-discord-client-id",
        "oauth-new-provider-client-id"
      ]
    }
  }
}
```

### Issue: "Rate limit exceeded"

**Symptoms:**
```json
HTTP 429: {"error": "Too many requests. Please try again later.", "retryAfterSeconds": 60}
```

**Cause:** Client exceeding rate limits.

**Rate Limits:**

| Policy | Limit | Window |
|--------|-------|--------|
| SecretsRead | 100 requests | 1 minute |
| SecretsWrite | 20 requests | 1 minute |
| SecretsRotate | 5 requests | 1 minute |

**Resolution:**

1. Implement exponential backoff in client
2. Cache secrets client-side (respect 5-minute TTL)
3. Use batch endpoint for multiple secrets:

```bash
# Instead of 10 individual calls
curl -X POST \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"secretNames": ["secret1", "secret2", "secret3"]}' \
  https://api.meridianconsole.com/api/v1/secrets/batch
```

### Issue: "Authorization denied"

**Symptoms:**
```json
HTTP 403: Forbidden
```

**Diagnosis:**

1. **Check token claims:**

```bash
# Decode JWT and check permissions
echo "<token>" | cut -d. -f2 | base64 -d | jq .
```

2. **Verify required permission:**

| Operation | Required Permission |
|-----------|---------------------|
| Read secret | `secrets:read:{category}` or `secrets:read:{name}` |
| Write secret | `secrets:write:{category}` or `secrets:write:{name}` |
| Rotate secret | `secrets:rotate:{category}` or `secrets:rotate:{name}` |
| Delete secret | `secrets:write:{category}` or `secrets:write:{name}` |
| Read certificates | `secrets:read:certificates` |
| Write certificates | `secrets:write:certificates` |
| Read vaults | `keyvault:read` |
| Manage vaults | `keyvault:write` |

**Resolution:**

Add required permission to user's role in Identity service.

### Issue: "Secret value too large"

**Symptoms:**
```json
HTTP 400: {"error": "Secret value exceeds 25600 byte limit"}
```

**Cause:** Azure Key Vault has a 25KB (25,600 bytes) limit for secret values.

**Resolution:**

1. Compress or split large values
2. Store large data in Azure Blob Storage, reference URL in secret
3. Consider using Key Vault certificates for X.509 certificates (supports up to 200KB)

### Issue: "Development provider has no secrets"

**Symptoms:** Getting `null` for all secrets in development.

**Cause:** `DevelopmentSecretProvider` is in-memory only; secrets must be seeded.

**Resolution:**

Seed secrets via API or configure to use real Key Vault:

```bash
# Option 1: Use real Key Vault in development
dotnet user-secrets set "Secrets:UseDevelopmentProvider" "false"
dotnet user-secrets set "Secrets:KeyVaultUri" "https://dev-vault.vault.azure.net/"

# Option 2: Seed via API (requires authentication bypass or dev token)
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"value": "test-value"}' \
  http://localhost:5110/api/v1/secrets/oauth-discord-client-id
```

---

## Monitoring and Alerts

### Key Metrics

| Metric | Warning Threshold | Critical Threshold |
|--------|-------------------|-------------------|
| Secret read latency (p95) | >200ms | >500ms |
| Secret write latency (p95) | >500ms | >1000ms |
| Key Vault error rate | >1% | >5% |
| Rate limit rejections | >10/min | >50/min |
| Authorization denials | >5% | >10% |
| Break-glass usage | Any | - |

### Grafana Dashboard Panels

1. **Request rate by endpoint** (req/sec)
2. **Latency by operation** (p50, p95, p99)
3. **Error rate by category** (4xx, 5xx)
4. **Rate limit rejections** (429 responses)
5. **Authorization denials** (403 responses)
6. **Secret access by category** (oauth, betterauth, infrastructure)
7. **Key Vault operation latency** (Azure SDK metrics)
8. **Cache hit rate**

### Log Queries (Loki)

```logql
# All secret access denials
{service="Dhadgar.Secrets"} |= "Access DENIED"

# Break-glass access events
{service="Dhadgar.Secrets"} |= "BREAK-GLASS"

# Key Vault errors
{service="Dhadgar.Secrets"} |= "Key Vault" |= "error"

# Rotation events
{service="Dhadgar.Secrets"} |= "Rotated secret"
```

### Alerting Rules

```yaml
# Prometheus AlertManager rules
groups:
  - name: secrets-service
    rules:
      - alert: SecretsServiceHighErrorRate
        expr: rate(http_server_requests_total{service="secrets",status=~"5.."}[5m]) > 0.05
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "Secrets service error rate above 5%"

      - alert: SecretsServiceHighLatency
        expr: histogram_quantile(0.95, rate(http_server_duration_seconds_bucket{service="secrets"}[5m])) > 0.5
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Secrets service p95 latency above 500ms"

      - alert: BreakGlassAccessDetected
        expr: increase(secrets_breakglass_access_total[1h]) > 0
        labels:
          severity: info
        annotations:
          summary: "Break-glass access detected - review required"
```

---

## Security Considerations

### Secret Value Protection

- Secret values are **never logged** (only names and metadata)
- In-memory cache cleared on service shutdown
- 5-minute cache TTL limits exposure window
- All access logged with user identity for audit

### Permission Model

The service enforces a hierarchical permission model:

1. `secrets:*` - Full admin access
2. `secrets:{action}:*` - Action on all categories
3. `secrets:{action}:{category}` - Action on category
4. `secrets:{action}:{secretName}` - Action on specific secret

### Audit Log Retention

- Audit logs retained for **90 days** minimum
- Break-glass events retained for **1 year**
- All logs exportable for compliance (SIEM integration)

### Network Security

- **Production**: HTTPS only (enforced by Gateway)
- **Internal**: mTLS recommended between services
- **Key Vault**: Network ACLs restrict to known IPs/VNets

### Incident Response

1. **Suspected compromise**: Immediately rotate affected secrets
2. **Unauthorized access**: Review audit logs, revoke tokens
3. **Data breach**: Follow organization's incident response plan

---

## Contact and Escalation

| Issue | Contact | SLA |
|-------|---------|-----|
| Routine operations | DevOps team | 8 hours |
| Production outage | On-call engineer | 15 minutes |
| Security incident | Security team | Immediate |
| Azure Key Vault issues | Azure Support + DevOps | 1 hour |

---

**End of Runbook**
