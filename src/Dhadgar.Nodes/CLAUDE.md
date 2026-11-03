# Nodes Service

Hardware inventory, agent enrollment, and health monitoring for customer-owned nodes.

## Tech Stack

- ASP.NET Core Minimal API
- PostgreSQL with EF Core
- MassTransit for messaging
- mTLS for agent authentication

## Port

5040

## Database

`dhadgar_platform` - Migrations in `Data/Migrations/`

## Key Entities

- Node, NodeHealth, NodeHardwareInventory, NodeCapacity
- EnrollmentToken, AgentCertificate, CapacityReservation
- NodeAuditLog

## Key Files

- `Endpoints/` - NodesEndpoints, AgentEndpoints, EnrollmentEndpoints, ReservationEndpoints
- `Services/` - HeartbeatService, EnrollmentService, CertificateAuthorityService
- `BackgroundServices/` - StaleNodeDetectionService, ReservationExpiryService

## Dependencies

- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
- Dhadgar.Messaging

## Notes

- Applies migrations in the background in Development mode (retrying while the database is down); never outside Development. `/readyz` is 503 until the database answers (`nodes-database` check)
- Stale nodes marked offline after 5 minutes without heartbeat
- Enrollment tokens default to 1 hour validity
- Agent certificates valid for 90 days
- CA initializes on startup; creates self-signed CA if none exists
