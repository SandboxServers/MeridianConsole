# Service Ports Reference

Quick reference guide for all service ports in Meridian Console (Dhadgar).

> **Canonical scheme:** ports follow the `50x0` pattern below. This table, the Gateway's
> YARP cluster config, `launchSettings.json`, the Helm chart, and the compose files all
> agree on these values. (Before 2026-07 the `launchSettings.json` files used a divergent
> sequential `5001–5012` scheme, which broke local Gateway routing; they were fixed to
> match this table.)

---

## Core Services

| Service | Port | Description | Database |
|---------|------|-------------|----------|
| **Gateway** | 5000 | API entry point, YARP reverse proxy | No |
| **Identity** | 5010 | User/org management, roles, OAuth, OpenIddict token issuance | PostgreSQL (`dhadgar-identity`) |
| **Nodes** | 5040 | Agent enrollment, mTLS CA, heartbeats, capacity reservations | PostgreSQL (`dhadgar-platform`) |
| **Secrets** | 5110 | Secret management, Azure Key Vault integration | No |
| **BetterAuth** | 5130 | Social OAuth authentication (Node.js/Express) | PostgreSQL (shared with Identity) |

---

## Implemented Supporting Services

| Service | Port | Description | Database |
|---------|------|-------------|----------|
| **Notifications** | 5090 | Email (Office 365/SMTP), alert dispatch, MassTransit consumers | PostgreSQL (`dhadgar-platform`) |
| **Discord** | 5120 | Discord bot, slash commands, platform health reporting | PostgreSQL (`dhadgar-platform`) |

---

## Stub Services

| Service | Port | Description | Database |
|---------|------|-------------|----------|
| **Billing** | 5020 | Subscription management, usage metering (planned) | No |
| **Servers** | 5030 | Game server lifecycle management (implementation open in PR #88) | No |
| **Tasks** | 5050 | Background job orchestration (planned) | No |
| **Files** | 5060 | File operations (slated for removal — see issue #115) | No |
| **Console** | 5070 | Real-time server console via SignalR (implementation open in PR #88) | No |
| **Mods** | 5080 | Mod registry, versioning (implementation open in PR #88) | No |

---

## Frontend Apps

| App | Port | Description | Type |
|-----|------|-------------|------|
| **Scope** | 4321 | Documentation site | Astro/React/Tailwind (static) |
| **Panel** | 4321 | Main control plane UI (scaffolding) | Astro/React/Tailwind (SSR) |
| **ShoppingCart** | 4322 | Marketing, pricing & profile | Astro/React/Tailwind (static) |

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

**Run the full application stack (including BetterAuth and frontends):**
```bash
docker compose -f deploy/compose/docker-compose.services.yml up -d
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
- Scalar API docs (aggregated): http://localhost:5000/scalar/v1

---

## Notes

- **All default credentials** for local services: `dhadgar` / `dhadgar` (being removed by PR #127 — services will require explicit credentials)
- **Gateway** (port 5000) is the single public entry point; it proxies to all microservices
- **Databases**: Identity and Billing get dedicated databases; Nodes, Servers, Tasks, Mods, Notifications and Discord currently share `dhadgar-platform` (see divergence note in ADR-0005/0006)
- **Frontend apps** use npm/Node.js; run `npm run dev` in their directories
- **BetterAuth** is a Node.js process; it is *not* started by the Aspire AppHost — run it via `npm start` in `src/Dhadgar.BetterAuth` or via `docker-compose.services.yml`
