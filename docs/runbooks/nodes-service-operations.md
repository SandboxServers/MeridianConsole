# Nodes Service Operations Runbook

This runbook covers operational procedures for the Meridian Console Nodes service, which manages hardware inventory, agent enrollment, and health monitoring.

## Health Check Endpoints

### Available Endpoints

|Endpoint|Purpose|Expected Response|
|---|---|---|
|`/healthz`|Basic health|200 OK with service info|
|`/livez`|Kubernetes liveness|200 OK|
|`/readyz`|Kubernetes readiness|200 OK with dependency status|

### Health Check Details

**`/healthz`** - Returns basic health information:

```json
{
  "status": "Healthy",
  "service": "Dhadgar.Nodes",
  "version": "1.0.0",
  "timestamp": "2026-01-28T12:00:00Z"
}
```

**`/readyz`** - Checks critical dependencies:
- PostgreSQL database connectivity
- RabbitMQ message bus connectivity
- Certificate Authority initialization status

## Node Lifecycle Management

### Node States

| State | Value | Description | Action |
|-------|-------|-------------|--------|
| Enrolling | 0 | Agent completing enrollment | Wait for completion |
| Online | 1 | Healthy, receiving heartbeats | Normal operation |
| Degraded | 2 | High CPU/memory/disk usage | Investigate resource usage |
| Offline | 3 | No heartbeat in 5 minutes | Check agent status |
| Maintenance | 4 | Intentionally offline | Planned maintenance |
| Decommissioned | 5 | Permanently removed | No action needed |

### Checking Node Status

```bash
# List all nodes for an organization
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes"

# Get specific node details
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}"

# Filter by status
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes?status=Offline"
```

### Maintenance Mode

**Enter Maintenance Mode:**
```bash
curl -X POST -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}/maintenance"
```

**Exit Maintenance Mode:**
```bash
curl -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}/maintenance"
```

**When to Use:**
- Before agent upgrades
- Before OS updates
- During hardware maintenance
- During network changes

### Decommissioning a Node

```bash
curl -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}"
```

**Before Decommissioning:**
1. Migrate or stop all game servers on the node
1. Release any active capacity reservations
1. Revoke agent certificate (optional, automatic on decommission)

## Agent Enrollment

### Creating Enrollment Tokens

```bash
# Create token with default expiration (1 hour)
curl -X POST -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"label": "Server Room A"}' \
  "http://gateway:5000/api/v1/organizations/{orgId}/enrollment/tokens"
```

Response:
```json
{
  "tokenId": "uuid",
  "token": "plaintext-token-only-shown-once",
  "expiresAt": "2026-01-28T13:00:00Z"
}
```

### Listing Active Tokens

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/enrollment/tokens"
```

### Revoking Tokens

```bash
curl -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/enrollment/tokens/{tokenId}"
```

### Troubleshooting Enrollment Failures

| Error | Cause | Resolution |
|-------|-------|------------|
| 401 Invalid token | Token expired or revoked | Create new token |
| 400 Invalid platform | Platform not linux/windows | Check agent configuration |
| 409 Node name conflict | Duplicate name in org | Agent will auto-generate unique name |

## Certificate Management

### Certificate Lifecycle

| Stage | Duration | Action |
|-------|----------|--------|
| Initial issue | During enrollment | Automatic |
| Valid | 90 days default | Normal operation |
| Renewal window | Last 30 days | Agent auto-renews |
| Expired | After expiry | Agent cannot authenticate |

### Checking Certificate Status

Certificates are tracked in the `agent_certificates` table:
```sql
SELECT node_id, thumbprint, issued_at, expires_at, is_active, revoked_at
FROM agent_certificates
WHERE node_id = 'uuid'
ORDER BY issued_at DESC;
```

### Manual Certificate Revocation

If a certificate is compromised:

1. **Revoke via API:**

```bash
curl -X POST -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason": "Security incident"}' \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}/certificates/revoke"
```

1. **Verify revocation in database:**

```sql
SELECT * FROM agent_certificates
WHERE node_id = 'uuid' AND revoked_at IS NOT NULL;
```

1. **Force agent re-enrollment** by creating new enrollment token

### CA Certificate Rotation

The Certificate Authority certificate is valid for 10 years by default. To rotate:

1. **Backup current CA:**
```bash
# Local storage
cp -r /app/CA /backup/CA-$(date +%Y%m%d)

# Azure Key Vault
az keyvault certificate backup --vault-name $VAULT --name meridian-agent-ca -f ca-backup.blob
```

1. **Delete CA files/secrets** (service will regenerate)

1. **Restart service:**
```bash
kubectl rollout restart deployment/nodes
```

1. **Re-enroll all agents** - existing certificates will be invalid

## Health Monitoring

### Heartbeat Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `HeartbeatThresholdMinutes` | 5 | Minutes before node marked offline |
| `StaleNodeCheckIntervalMinutes` | 1 | Background check frequency |

### Health Degradation Thresholds

| Metric | Threshold | Action |
|--------|-----------|--------|
| CPU | 90% | Node marked as Degraded |
| Memory | 90% | Node marked as Degraded |
| Disk | 90% | Node marked as Degraded |

### Health Score Calculation

Health score (0-100) is calculated from:
- CPU usage (weight: 30%)
- Memory usage (weight: 30%)
- Disk usage (weight: 20%)
- Health issues count (weight: 20%)

### Investigating Offline Nodes

1. **Check last heartbeat:**
```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}"
```
Look at `lastHeartbeat` timestamp.

1. **Check agent logs on customer hardware:**
```bash
# Linux
journalctl -u meridian-agent -f

# Windows
Get-EventLog -LogName Application -Source "Meridian Agent" -Newest 50
```

1. **Common causes:**
   - Network connectivity issues
   - Agent process crashed
   - Certificate expired
   - Firewall blocking outbound HTTPS

## Capacity Reservations

### Viewing Active Reservations

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}/reservations"
```

### Checking Available Capacity

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/organizations/{orgId}/nodes/{nodeId}/reservations/capacity"
```

Response:
```json
{
  "totalMemoryMb": 32768,
  "availableMemoryMb": 24576,
  "totalDiskMb": 512000,
  "availableDiskMb": 409600,
  "totalCpuMillicores": 8000,
  "availableCpuMillicores": 6000,
  "activeReservations": 2
}
```

### Releasing Stuck Reservations

If a reservation is stuck (deployment failed):

```bash
curl -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://gateway:5000/api/v1/reservations/{reservationToken}"
```

### Reservation Lifecycle

| Status | Description | TTL |
|--------|-------------|-----|
| Pending | Awaiting claim | 15 minutes default |
| Claimed | Bound to server | Indefinite |
| Released | Explicitly released | Immediate cleanup |
| Expired | Timeout without claim | Background cleanup |

## Database Operations

### Connection String

```ini
Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar
```

### Common Queries

**Nodes per organization:**
```sql
SELECT organization_id, status, COUNT(*) as count
FROM nodes
WHERE deleted_at IS NULL
GROUP BY organization_id, status;
```

**Stale nodes (should be offline):**
```sql
SELECT id, name, status, last_heartbeat
FROM nodes
WHERE status = 1 -- Online
  AND last_heartbeat < NOW() - INTERVAL '5 minutes'
  AND deleted_at IS NULL;
```

**Expired enrollment tokens:**
```sql
SELECT id, label, expires_at, created_by_user_id
FROM enrollment_tokens
WHERE used_at IS NULL
  AND is_revoked = false
  AND expires_at < NOW();
```

**Capacity reservation utilization:**
```sql
SELECT node_id,
       SUM(memory_mb) as reserved_memory,
       SUM(disk_mb) as reserved_disk,
       SUM(cpu_millicores) as reserved_cpu
FROM capacity_reservations
WHERE status IN (0, 1) -- Pending or Claimed
GROUP BY node_id;
```

### Migrations

```bash
# Apply pending migrations
dotnet ef database update \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes

# Check migration status
dotnet ef migrations list \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes
```

**Note:** Migrations auto-apply in Development mode.

## Performance Monitoring

### Key Metrics

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `nodes_enrolled_total` | Total enrolled nodes | Anomaly detection |
| `nodes_online_count` | Currently online nodes | Drop > 20% |
| `heartbeats_processed_total` | Heartbeats received | Rate anomaly |
| `enrollment_failures_total` | Failed enrollments | > 10/hour |
| `certificates_issued_total` | Certs issued | Spike detection |

### Prometheus Queries

**Nodes by status:**
```promql
sum(nodes_by_status) by (status)
```

**Heartbeat processing rate:**
```promql
rate(heartbeats_processed_total[5m])
```

**Enrollment success rate:**
```promql
sum(rate(enrollment_success_total[1h])) /
(sum(rate(enrollment_success_total[1h])) + sum(rate(enrollment_failures_total[1h])))
```

## Incident Response

### Mass Node Offline

If many nodes go offline simultaneously:

1. **Check service health:**
```bash
curl http://nodes:5040/healthz
```

1. **Check RabbitMQ connectivity** - heartbeat events may be backing up

1. **Check recent deployments** - configuration changes may affect agent auth

1. **Review stale node detection logs:**
```bash
kubectl logs -l app=nodes --since=15m | grep "StaleNodeDetection"
```

### Enrollment Spike

If enrollment requests spike unexpectedly:

1. **Check rate limiting** at Gateway level

1. **Review token creation audit logs:**
```sql
SELECT * FROM node_audit_logs
WHERE action = 'EnrollmentTokenCreated'
ORDER BY occurred_at DESC
LIMIT 100;
```

1. **Consider temporary token revocation** if malicious

### Certificate Authority Compromise

If CA private key is compromised:

1. **Immediately revoke all certificates:**
```sql
UPDATE agent_certificates SET revoked_at = NOW(), revocation_reason = 'CA compromise'
WHERE revoked_at IS NULL;
```

1. **Rotate CA** (see CA Certificate Rotation above)

1. **Re-enroll all agents** with new tokens

1. **Audit all actions** during compromise window

## Backup and Recovery

### Data to Backup

| Data | Location | Frequency |
|------|----------|-----------|
| Database | PostgreSQL | Daily |
| CA Certificate | Local/Key Vault | On change |
| Configuration | Git/ConfigMaps | On change |

### Database Backup

```bash
pg_dump -h localhost -U dhadgar dhadgar_platform > backup.sql
```

### Recovery Steps

1. **Restore database:**
```bash
psql -h localhost -U dhadgar dhadgar_platform < backup.sql
```

1. **Restore CA certificate** (if using local storage)

1. **Restart service:**
```bash
kubectl rollout restart deployment/nodes
```

1. **Verify agent connectivity** - agents should reconnect automatically

## Maintenance Procedures

### Rolling Restart

```bash
kubectl rollout restart deployment/nodes
kubectl rollout status deployment/nodes
```

### Configuration Update

1. Update `appsettings.json` or environment variables
1. Deploy new configuration
1. Verify with `/healthz`
1. Monitor for errors

### Scaling

```bash
# Scale up
kubectl scale deployment/nodes --replicas=3

# Verify
kubectl get pods -l app=nodes
```

The Nodes service is stateless (state is in database) and scales horizontally.

### Background Service Status

Three background services run in the Nodes service:

| Service | Purpose | Interval |
|---------|---------|----------|
| StaleNodeDetectionService | Mark offline nodes | 1 minute |
| ReservationExpiryService | Clean expired reservations | 1 minute |
| AuditLogCleanupService | Prune old audit logs | 1 hour |

Check logs for background service health:
```bash
kubectl logs -l app=nodes | grep -E "(StaleNode|Reservation|AuditLog)"
```
