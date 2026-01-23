# Nodes Service

Hardware inventory, agent enrollment, and health monitoring for the Meridian Console platform.

## Tech Stack

- ASP.NET Core Minimal API
- PostgreSQL with EF Core
- MassTransit for messaging

## Port

5040

## Status

Implemented - core functionality complete.

## Key Features

- Node registration and lifecycle management (Online, Degraded, Offline, Maintenance, Decommissioned)
- Agent enrollment with one-time tokens
- Health monitoring via heartbeats
- Capacity tracking for resource allocation
- **Capacity reservation system** for preventing over-provisioning
- Certificate management for agent authentication

## Entities

- **Node** - Primary entity representing customer hardware
- **NodeHardwareInventory** - Hardware specs (CPU, memory, disk)
- **NodeHealth** - Current health metrics from heartbeats
- **NodeCapacity** - Capacity tracking for game server allocation
- **CapacityReservation** - Temporary capacity locks for deployment workflows
- **EnrollmentToken** - One-time tokens for agent enrollment
- **AgentCertificate** - mTLS certificates for agent authentication

## API Endpoints

### Node Management (User-facing)

- `GET /api/v1/organizations/{orgId}/nodes` - List nodes
- `GET /api/v1/organizations/{orgId}/nodes/{id}` - Get node details
- `PATCH /api/v1/organizations/{orgId}/nodes/{id}` - Update node
- `DELETE /api/v1/organizations/{orgId}/nodes/{id}` - Decommission node
- `POST /api/v1/organizations/{orgId}/nodes/{id}/maintenance` - Enter maintenance
- `DELETE /api/v1/organizations/{orgId}/nodes/{id}/maintenance` - Exit maintenance

### Enrollment (User-facing)

- `POST /api/v1/organizations/{orgId}/enrollment/tokens` - Create token
- `GET /api/v1/organizations/{orgId}/enrollment/tokens` - List active tokens
- `DELETE /api/v1/organizations/{orgId}/enrollment/tokens/{id}` - Revoke token

### Agent (Agent-facing)

- `POST /api/v1/agents/enroll` - Enroll new agent (uses token)
- `POST /api/v1/agents/{nodeId}/heartbeat` - Report health
- `POST /api/v1/agents/{nodeId}/certificates/renew` - Renew mTLS certificate
- `GET /api/v1/agents/ca-certificate` - Get CA certificate (public, no auth)

### Capacity Reservations (Service-to-service)

- `POST /api/v1/organizations/{orgId}/nodes/{nodeId}/reservations` - Create reservation
- `GET /api/v1/organizations/{orgId}/nodes/{nodeId}/reservations` - List active reservations
- `GET /api/v1/organizations/{orgId}/nodes/{nodeId}/reservations/capacity` - Get available capacity
- `GET /api/v1/reservations/{token}` - Get reservation by token
- `POST /api/v1/reservations/{token}/claim` - Claim with server ID
- `DELETE /api/v1/reservations/{token}` - Release reservation

## Events Published

- `NodeEnrolled`, `NodeOnline`, `NodeOffline`, `NodeDegraded`, `NodeRecovered`
- `NodeDecommissioned`, `NodeMaintenanceStarted`, `NodeMaintenanceEnded`
- `AgentCertificateIssued`, `AgentCertificateRevoked`, `AgentCertificateRenewed`

## Database

`dhadgar_platform` - Migrations in `Data/Migrations/`

## Dependencies

- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
- Dhadgar.Messaging

## Certificate Authority

The Nodes service includes a private CA for mTLS authentication between agents and the control plane:

- **CA Storage**: Local file-based (development) or Azure Key Vault (production)
- **Certificate Validity**: 90 days (configurable via `CertificateValidityDays`)
- **Key Size**: CA uses 4096-bit RSA, client certs use 2048-bit RSA
- **SPIFFE ID**: Certificates include `spiffe://meridianconsole.com/nodes/{nodeId}` in SAN
- **Extended Key Usage**: Client Authentication (OID 1.3.6.1.5.5.7.3.2)

### Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `CaStorageType` | "local" | "local" or "azurekeyvault" |
| `CaStoragePath` | AppData/MeridianConsole/CA | Local CA storage path |
| `CaKeyPassword` | auto-generated | Password for CA private key |
| `CaKeySize` | 4096 | RSA key size for CA |
| `CaValidityYears` | 10 | CA certificate validity |
| `ClientKeySize` | 2048 | RSA key size for client certs |
| `CertificateValidityDays` | 90 | Client certificate validity |

## Notes

- Auto-applies migrations in Development mode
- Stale nodes marked offline after 5 minutes without heartbeat
- Enrollment tokens default to 1 hour validity
- Agent certificates valid for 90 days (configurable)
- CA initializes on startup; creates self-signed CA if none exists
