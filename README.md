# Meridian Console (Solution: **Dhadgar**)

Meridian Console is a modern, security-first **game server control plane**: a multi-tenant web UI + API gateway + microservices that coordinate **customer-hosted agents** to provision, run, and manage game servers on user-owned hardware.

This repository is the **bootstrapped codebase** (solution name **Dhadgar**) for the product described in the scope/architecture document. Today it’s mostly “hello world” scaffolding with the correct shape: services, shared libraries, agents, tests, EF Core wiring, and local dev infrastructure.

> **Codenames**
> - **Dadgar**: pre-release codename used in the scope doc (SaaS-first)
> - **KiP**: “Knowledge is Power” self-host edition (planned post-MVP)
>
> The solution/project prefix in this repo is **Dhadgar** (requested naming for the codebase). Treat this as “code name vs product name.”

---

## Project status: what works today vs what’s planned

This repo is intentionally early-stage scaffolding.

### ✅ Working today
- `dotnet restore / build / test` across the solution (once local prerequisites are installed)
- Every service has basic endpoints:
  - `GET /` (service banner)
  - `GET /hello` (string hello)
  - `GET /healthz` (health probe)
- Swagger enabled for ASP.NET services in Development
- Gateway has YARP wired and can load routes from configuration
- Console service has a SignalR hub stub
- EF Core DbContexts exist for DB-backed services (with migrations folder conventions)
- Local dev dependencies via Docker Compose: PostgreSQL + RabbitMQ + Redis
- **One xUnit test project per project**, with a basic “hello world” test

### 🚧 Planned (not implemented yet)
- Real auth flows, RBAC policy enforcement, user/org lifecycle
- Real billing (SaaS), usage metering, invoices
- Actual provisioning workflows, scheduling, node capacity management
- Agent enrollment + certificate issuance/rotation (mTLS), secure remote execution
- Real message topology and consumers (MassTransit), retries, DLQs, idempotency
- Real Web UI features (beyond scaffolding)
- Observability stack (structured logging, tracing, dashboards), alerting, audit trails
- Kubernetes manifests / Helm charts / GitOps

If you’re onboarding: assume the code is **shape-first** and features will land incrementally.

---

## What Meridian Console is (and isn’t)

### It **is**
- A **panel/control plane** for orchestrating workloads (starting with game servers)
- A multi-tenant system (SaaS-first) with a planned self-host edition (KiP)
- Designed to manage **customer-owned hardware** via agents

### It **is not**
- A “hosted game server provider” by itself — it orchestrates nodes you control
- A mature, production-ready product today — it’s a scaffolded foundation
- A monolith — services integrate via runtime contracts, not compile-time references

---

## Trust boundaries & security model (intent + current reality)

### Trust boundaries (how we think about it)
- **Agents run on customer hardware** and must be treated as high-trust local components.
- The control plane should be able to:
  - issue commands *to* agents,
  - receive health/metrics/events *from* agents,
  - but **not** silently expand privileges or access beyond what the customer installed.

### Network posture (target)
- Ideally: agent makes **outbound** connections to the control plane (no inbound holes required).
- The scope intends **TLS everywhere**:
  - User traffic: TLS at the edge (e.g., Cloudflare) into the cluster
  - Internal traffic: mTLS (service mesh / CNI policy in the scope)
  - Agent traffic: **mTLS**, cert issuance/rotation as a first-class capability

### Data collection (target)
- Minimum required: node health, resource utilization, job status, server lifecycle events
- Explicitly avoid collecting customer content unless required (e.g., logs a user requests, or files they upload)

### Where this is today
- This repo contains stubs for services and agents; the above is the **design intent**.
- Anything related to cert issuance, mTLS bootstrap, and audit-grade security is **planned work**.

---

## Architecture (target shape)

At a high level:
- Edge layer (e.g., Cloudflare): WAF/CDN/DDoS, user TLS termination
- **Gateway**: single entry point into the cluster (routing, auth enforcement, rate limiting)
- Microservices inside Kubernetes (dev/staging/prod separated by namespaces)
- Customer-hosted **Agents** (Linux + Windows) on isolated VLANs connecting back securely
- Async backbone via **RabbitMQ + MassTransit** (commands/events)

---

## Services (planned responsibilities)

> **Important:** The scope doc defines canonical internal ports. This repo does not hard-pin ports yet.
> Standardize ports via `ASPNETCORE_URLS`, `launchSettings.json`, or Kubernetes service config.

| Service | Project | Purpose |
|---|---|---|
| Gateway | `src/Dhadgar.Gateway` | Only public entry point; YARP routing and policy enforcement |
| Identity | `src/Dhadgar.Identity` | AuthN/AuthZ, JWT issuance/validation, RBAC |
| Billing | `src/Dhadgar.Billing` | SaaS subscriptions/payments (excluded or disabled in KiP) |
| Servers | `src/Dhadgar.Servers` | Game server lifecycle & templates |
| Nodes | `src/Dhadgar.Nodes` | Node inventory, health, capacity, agent coordination |
| Tasks | `src/Dhadgar.Tasks` | Orchestration and background jobs |
| Files | `src/Dhadgar.Files` | File metadata + transfer orchestration |
| Console | `src/Dhadgar.Console` | Real-time console streaming (SignalR) |
| Mods | `src/Dhadgar.Mods` | Mod registry/versioning/install orchestration |
| Notifications | `src/Dhadgar.Notifications` | Email/Discord/webhook notifications |
| Firewall | `src/Dhadgar.Firewall` | Port/policy intent and safety rails |
| Secrets | `src/Dhadgar.Secrets` | Secret storage + rotation + audit intent |
| Discord | `src/Dhadgar.Discord` | Discord integration services |
| Panel UI | `src/Dhadgar.Panel` | Blazor Web UI (WASM) for the main panel |
| Shopping Cart | `src/Dhadgar.ShoppingCart` | SWA marketing + checkout site with /api functions |
| Scope | `src/Dhadgar.Scope` | Static host for the scope/doc HTML |
| CLI | `src/Dhadgar.Cli` | Operator-style CLI (“azure cli”-like) |
| Linux Agent | `src/Agents/Dhadgar.Agent.Linux` | Customer-side agent (Linux) |
| Windows Agent | `src/Agents/Dhadgar.Agent.Windows` | Customer-side agent (Windows) |
| Agent Core | `src/Agents/Dhadgar.Agent.Core` | Shared agent logic |

### Shared libraries
| Library | Project | What it’s for |
|---|---|---|
| Shared | `src/Shared/Dhadgar.Shared` | Cross-cutting primitives/utilities |
| Contracts | `src/Shared/Dhadgar.Contracts` | DTOs and message/API contracts |
| Messaging | `src/Shared/Dhadgar.Messaging` | MassTransit/RabbitMQ conventions and helpers |
| ServiceDefaults | `src/Shared/Dhadgar.ServiceDefaults` | Common service wiring defaults |

---

## Runtime dependencies vs compile-time dependencies (important)

Section 13’s “service dependencies” describe **runtime relationships** (who calls who, who publishes/subscribes, which infra is required).

This repo intentionally does **not** wire microservices together via `ProjectReference` (e.g., Gateway should **not** reference Identity’s service project).

### Why we don’t do service→service project references
Direct project references between services:
- couple deployment/versioning (you’ve effectively made a distributed monolith),
- encourage “shared internals” instead of stable contracts,
- make it harder to run/scale services independently.

### What we do instead (compile-time)
Services should depend only on:
- **shared contracts** (`Dhadgar.Contracts`)
- messaging conventions (`Dhadgar.Messaging`)
- shared primitives (`Dhadgar.Shared`)
- shared hosting defaults (`Dhadgar.ServiceDefaults`)
- optionally, **client libraries** (recommended future) like `Dhadgar.Identity.Client` (HTTP API client) that reference only contracts.

### What “service dependencies” become in code
- HTTP: typed clients configured by base URLs
- Messaging: publish/consume contracts via MassTransit
- Health/readiness: downstream checks (optional)
- Config: service discovery / base URLs / auth settings

---

## Configuration reference (where to set things)

Configuration follows standard ASP.NET Core conventions:
- `appsettings.json` + `appsettings.Development.json` per service (recommended)
- environment variables (`ConnectionStrings__Postgres`, etc.)
- user secrets for local dev (recommended for tokens/keys)
- Kubernetes secrets/config maps later

### Where config files live
Per service:
- `src/<ServiceName>/appsettings.json`
  - Example: `src/Dhadgar.Gateway/appsettings.json`

Shared defaults:
- Repo-level: `Directory.Build.props`, `Directory.Packages.props`, `global.json`

Local dev infra:
- `deploy/compose/docker-compose.dev.yml`

### Common configuration keys (conventions)

#### PostgreSQL
Used by DB-backed services (Identity/Billing/Servers/Nodes/Tasks/Files/Mods/Notifications).

- Key:
  - `ConnectionStrings:Postgres`
- Where:
  - `src/<Service>/appsettings.Development.json` (recommended), or
  - environment variable: `ConnectionStrings__Postgres`

Example:
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar;Username=dhadgar;Password=dhadgar"
  }
}
```

#### RabbitMQ
Used for async commands/events (MassTransit).

Recommended keys (convention; implement as features land):
- `RabbitMq:Host`
- `RabbitMq:Username`
- `RabbitMq:Password`

Environment variable equivalents:
- `RabbitMq__Host`, `RabbitMq__Username`, `RabbitMq__Password`

#### Redis
Used for caching, ephemeral coordination, rate limiting, etc.

Recommended keys:
- `Redis:ConnectionString`
Environment variable:
- `Redis__ConnectionString`

#### Gateway (YARP routes)
- Where:
  - `src/Dhadgar.Gateway/appsettings.json`
- Key:
  - `ReverseProxy` (YARP config section)

#### Identity / JWT (planned)
Recommended keys:
- `Auth:Issuer`
- `Auth:Audience`
- `Auth:SigningKey` (store as secret)
Environment variables:
- `Auth__Issuer`, `Auth__Audience`, `Auth__SigningKey`

#### Discord integration (planned)
Recommended keys:
- `Discord:BotToken` (secret)
- `Discord:GuildId`
- `Discord:WebhookUrl` (optional)

### Local dev secrets
Use dotnet user-secrets for tokens/keys:
```bash
dotnet user-secrets init --project src/Dhadgar.Identity
dotnet user-secrets set "Auth:SigningKey" "dev-only-signing-key" --project src/Dhadgar.Identity
```

---

## Getting started

### Prerequisites
- .NET SDK (pinned by `global.json`)
- Docker (for local PostgreSQL/RabbitMQ/Redis)
- VS Code + C# Dev Kit recommended

### Start local dependencies
```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

### Build + test
```bash
dotnet restore
dotnet build
dotnet test
```

### Run a service (example: Gateway)
```bash
dotnet run --project src/Dhadgar.Gateway
```

---

## AI-Assisted Development with Claude Code

This repository is configured for AI-assisted development using [Claude Code](https://claude.ai/code). When working on this codebase, developers should leverage the specialized Claude agents for optimal results.

### Setup

Claude Code reads project-specific instructions from `CLAUDE.md` in the repository root. This file contains:
- Build and development commands
- Architecture guidelines and patterns
- Configuration conventions
- Testing strategies

### Specialized Agents

When using Claude Code in this repository, the following specialized agents are available:

| Agent | Use Case |
|-------|----------|
| **blazor-webdev-expert** | Blazor WebAssembly development, MudBlazor components, responsive layouts, forms, and styling |
| **dotnet-10-researcher** | .NET 10 features, EF Core patterns, security best practices, and API design guidance |
| **security-architect** | Authentication/authorization design, secrets management, mTLS configuration, threat modeling |
| **Explore** | Codebase exploration, finding files, understanding code structure |
| **Plan** | Designing implementation strategies for complex features |

### Recommended Workflows

**For new Blazor pages/components:**
```
Use the blazor-webdev-expert agent when creating UI components for Panel, ShoppingCart, or Scope
```

**For service implementation:**
```
Use dotnet-10-researcher for guidance on .NET 10 patterns, then implement with context from CLAUDE.md
```

**For security-sensitive features:**
```
Consult security-architect agent before implementing auth flows, agent enrollment, or secrets handling
```

**For documentation lookup:**
```
Use Context7 MCP tools to fetch up-to-date docs for MudBlazor, EF Core, MassTransit, and YARP
```

### Best Practices

1. **Read CLAUDE.md first** - It contains critical architecture decisions (e.g., no service-to-service project references)
2. **Use agents proactively** - Let specialized agents guide implementation for their domains
3. **Leverage Context7** - Fetch current library documentation instead of relying on training data
4. **Follow established patterns** - Check existing services before implementing new ones

---

## EF Core migrations (DB-backed services)

A service typically keeps migrations under `Data/Migrations`.

### Add a migration (example: Identity)
```bash
dotnet ef migrations add Init \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity \
  --output-dir Data/Migrations
```

### Apply migrations
```bash
dotnet ef database update \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity
```

> Some services may apply migrations automatically during Development startup (dev convenience).
> For production, prefer running migrations as a separate deployment step/job.

---

## Local repository structure

```
MeridianConsole/
  Dhadgar.sln        # Main .NET solution entry point.
  global.json        # SDK pinning and toolchain versioning.
  src/               # Core application code and shared libraries.
    Shared/          # Cross-cutting contracts, messaging, and defaults.
    Agents/          # Agent implementations and platform-specific runtimes.
  tests/             # Automated tests organized by feature area.
  deploy/            # Deployment templates and infrastructure scaffolding.
  docs/              # Public-facing documentation and architectural overviews.
```

---

## Roadmap (high level)

- SaaS MVP first (core panel + agents + orchestration)
- Phase 2/3 hardening and expansion (scale, observability, resilience)
- KiP (self-host) after SaaS MVP

Timelines are flexible—this is a passion project built to ship iteratively.

---

## License

TBD. Decide before any public/community push (especially if KiP is intended to be open-source).

---

## Support / Contact

TBD (issue tracker / Discord / security contact).
For now: open an issue in this repo with “dev environment” details and logs if you’re stuck.
