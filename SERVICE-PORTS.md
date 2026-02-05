# Service Ports Reference

Quick reference guide for all service ports in Meridian Console (Dhadgar).

---

## Core Services

| Service | Port | Description | Database |
|---------|------|-------------|----------|
| **Gateway** | 5000 | API entry point, YARP reverse proxy | No |
| **Identity** | 5010 | User/org management, roles, OAuth | PostgreSQL |
| **Servers** | 5030 | Game server lifecycle, templates, state machine | PostgreSQL |
| **Nodes** | 5040 | Agent enrollment, mTLS CA, health monitoring | PostgreSQL |
| **Console** | 5070 | Real-time server console via SignalR | PostgreSQL + Redis |
| **Mods** | 5080 | Mod registry, versioning, dependencies | PostgreSQL |
| **Secrets** | 5110 | Secret management, Azure Key Vault integration | No |
| **BetterAuth** | 5130 | Passwordless authentication | PostgreSQL (shared) |

---

## Stub Services

| Service | Port | Description | Database |
|---------|------|-------------|----------|
| **Billing** | 5020 | Subscription management, usage metering (planned) | No |
| **Tasks** | 5050 | Background job orchestration (planned) | No |
| **Files** | 5060 | File upload/download, mod distribution (planned) | No |
| **Notifications** | 5090 | Email, Discord, webhook notifications (planned) | No |
| **Discord** | 5120 | Discord bot integration (planned) | No |

---

## Frontend Apps

| App | Port | Description | Type |
|-----|------|-------------|------|
| **Scope** | 4321 | Documentation site | Astro/React/Tailwind |
| **Panel** | - | Main control plane UI (scaffolding) | Astro/React/Tailwind |
| **ShoppingCart** | - | Marketing & checkout (wireframe) | Astro/React/Tailwind |

---

## Local Infrastructure (Docker Compose)

| Service | Port(s) | Description | Credentials |
|---------|---------|-------------|-------------|
| **PostgreSQL** | 5432 | Database for microservices | `dhadgar` / `dhadgar` |
| **RabbitMQ** (AMQP) | 5672 | Message bus | `dhadgar` / `dhadgar` |
| **RabbitMQ** (Management UI) | 15672 | RabbitMQ admin console | `dhadgar` / `dhadgar` |
| **Redis** | 6379 | Caching and sessions | (no auth) |
| **Grafana** | 3000 | Metrics dashboards | `admin` / `admin` |
| **Prometheus** | 9090 | Metrics collection | (no auth) |
| **Loki** | 3100 | Log aggregation | (no auth) |
| **OpenTelemetry Collector** | 4317 | gRPC telemetry endpoint | (no auth) |
| **OpenTelemetry Collector** | 4318 | HTTP telemetry endpoint | (no auth) |

---

## Quick Links

**Start local infrastructure:**
```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

**Run Gateway:**
```bash
dotnet run --project src/Dhadgar.Gateway
# Runs on http://localhost:5000
```

**Access dashboards:**
- Grafana: http://localhost:3000 (admin/admin)
- Prometheus: http://localhost:9090
- RabbitMQ: http://localhost:15672 (dhadgar/dhadgar)
- Swagger (all services): http://localhost:5000/swagger (when routed through Gateway)

---

## Notes

- **All default credentials** for local services: `dhadgar` / `dhadgar`
- **Gateway** (port 5000) is the single public entry point; it proxies to all microservices
- **Database-per-service**: Each service owns its own schema
- **Frontend apps** use npm/Node.js; run `npm run dev` in their directories
- **Stub services** have basic scaffolding but core functionality is planned
