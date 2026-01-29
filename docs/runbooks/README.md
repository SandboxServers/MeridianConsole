# Runbooks

Operational runbooks for Meridian Console services.

## Available Runbooks

| Runbook | Service | Description |
|---------|---------|-------------|
| [Gateway Operations](./gateway-operations.md) | Gateway | YARP proxy, rate limiting, circuit breaker, diagnostics |
| [Identity Service Deployment](./identity-service-deployment.md) | Identity | User/org management, OAuth providers |
| [Nodes Service Operations](./nodes-service-operations.md) | Nodes | Hardware inventory, agent enrollment, health monitoring |
| [Secrets Service Operations](./secrets-service-operations.md) | Secrets | Azure Key Vault integration, audit logging |

## Runbook Structure

Each runbook follows a standard structure:
- Health Check Endpoints
- Common Operations
- Database Operations (if applicable)
- Performance Monitoring
- Incident Response
- Backup and Recovery
- Maintenance Procedures
