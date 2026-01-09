# Meridian Console - Development Roadmap

This document tracks the platform's development progress across five major phases.

---

## Phase 1: Infrastructure ✅ (COMPLETE)

**Goal**: Production-grade microservices foundation with full local development environment

### Infrastructure & DevEx
- ✅ **Docker Compose Orchestration** - Full 11-service stack + infrastructure
- ✅ **HTTPS-Enabled Local Development** - Dev certificates via `/https` volume mount
- ✅ **Built-in Diagnostic Endpoints** - `/diagnostics/*` for E2E testing
- ✅ **Development Secret Provider** - Local development without Azure Key Vault access
- ✅ **Azure Workload Identity Federation** - Kubernetes → Azure auth support

### Security & Authentication
- ✅ **Gateway (YARP)** - Reverse proxy with rate limiting, CORS, security headers
- ✅ **Identity Service** - ASP.NET Core Identity + OpenIddict OAuth server
- ✅ **Token Exchange Flow** - BetterAuth session → JWT conversion
- ✅ **Secrets Service** - Azure Key Vault integration with local dev fallback
- ✅ **JWT-Based Authentication** - All services validate JWTs via JWKS

### Services (Scaffolded + Healthy)
- ✅ **13 Microservices** - All with health checks, Swagger, basic endpoints
- ✅ **EF Core DbContexts** - Migration support for DB-backed services
- ✅ **MassTransit/RabbitMQ** - Wiring ready for async messaging
- ✅ **Redis Integration** - Caching and rate limiting infrastructure

### Key Deliverables
- **Docker Compose**: `deploy/compose/docker-compose.dev.yml` + `docker-compose.services.yml`
- **Diagnostic Endpoints**: Gateway E2E testing (`/diagnostics/integration`, `/diagnostics/services`, `/diagnostics/wif`)
- **Development Workflows**: Full local dev stack with `docker compose up`

---

## Phase 2: Core Features (IN PROGRESS)

**Goal**: Implement domain logic for server provisioning and management

### User & Organization Lifecycle
- [ ] User registration and onboarding
- [ ] Organization creation and management
- [ ] Team/role assignment within organizations
- [ ] Real RBAC policy enforcement (beyond JWT validation)

### Server Provisioning Workflows
- [ ] Server template management (game server configs)
- [ ] Provisioning workflow orchestration (Tasks service)
- [ ] Node capacity tracking and allocation
- [ ] Server lifecycle management (create, start, stop, delete)

### Agent Enrollment & Communication
- [ ] Agent enrollment flow (mTLS certificate issuance)
- [ ] Agent certificate rotation
- [ ] Secure command execution (control plane → agent)
- [ ] Agent health reporting and metrics collection

### Messaging Topology
- [ ] MassTransit command/event contracts
- [ ] Message consumers for domain events
- [ ] Retry policies and dead letter queues
- [ ] Idempotency and deduplication

### Data Models & Business Logic
- [ ] Server entity relationships (server → node → organization)
- [ ] Node inventory and health tracking
- [ ] Provisioning task state machine
- [ ] File transfer orchestration

### Testing
- [ ] Integration tests with WebApplicationFactory
- [ ] Message consumer unit tests
- [ ] End-to-end provisioning workflow tests

---

## Phase 3: Frontend Integration (PLANNED)

**Goal**: Enable Astro frontend applications to integrate with the backend platform

**Note**: Frontend applications (Scope, Shopping Cart, Panel) are developed in **separate repositories** using Astro + BetterAuth.

### Gateway API Routes
- [ ] Public API endpoints for Astro sites
- [ ] Versioned API routes (`/v1/servers`, `/v1/nodes`, etc.)
- [ ] OpenAPI/Swagger documentation for external consumers

### BetterAuth Integration
- [ ] BetterAuth session validation in Gateway
- [ ] Session → JWT token exchange middleware
- [ ] Nonce-based replay attack prevention (Redis)
- [ ] Session expiration and renewal

### Cross-Origin Support
- [ ] CORS configuration for production domains
  - `https://scope.meridianconsole.com`
  - `https://cart.meridianconsole.com`
  - `https://panel.meridianconsole.com`
  - `http://localhost:4321` (Astro dev server)
- [ ] Rate limiting per frontend application
- [ ] CSRF protection for state-changing operations

### Real-Time Features (Optional)
- [ ] WebSocket support for real-time updates
- [ ] SignalR hub for console streaming
- [ ] Server status push notifications

### API Design
- [ ] RESTful endpoint consistency
- [ ] Pagination, filtering, sorting conventions
- [ ] Error response standardization
- [ ] API versioning strategy

---

## Phase 4: SaaS Features (PLANNED)

**Goal**: Add commercial SaaS capabilities for monetization

### Billing Integration
- [ ] Stripe integration (Billing service)
- [ ] Subscription plans and pricing tiers
- [ ] Usage metering (server hours, storage, bandwidth)
- [ ] Invoice generation and payment processing

### Multi-Tenant Hardening
- [ ] Organization-level data isolation audits
- [ ] Tenant-scoped database queries (EF Core global filters)
- [ ] Resource quotas and limits enforcement
- [ ] Cost tracking per organization

### Observability Stack
- [ ] Structured logging (Serilog → centralized sink)
- [ ] Distributed tracing (OpenTelemetry)
- [ ] Metrics collection (Prometheus/New Relic)
- [ ] Dashboards and alerting (Grafana/New Relic)
- [ ] Audit trails (user actions, API calls)

### Production Deployment
- [ ] Kubernetes manifests (Deployments, Services, Ingress)
- [ ] Helm charts for templated deployments
- [ ] GitOps workflows (ArgoCD/Flux)
- [ ] Infrastructure as Code (Terraform/Bicep)

### Security Hardening
- [ ] Secrets rotation automation
- [ ] Network policies (CNI)
- [ ] Service mesh (Istio/Linkerd) for mTLS
- [ ] Penetration testing and remediation

---

## Phase 5: KiP (Self-Host Edition) (PLANNED)

**Goal**: Enable self-hosted "Knowledge is Power" deployment mode

### Billing Bypass
- [ ] Feature flag to disable Billing service
- [ ] Local user/org management (no SaaS restrictions)
- [ ] Unlimited resource quotas for self-host mode

### Single-Tenant Optimizations
- [ ] Simplified authentication (no multi-org isolation needed)
- [ ] Database schema optimizations for single tenant
- [ ] Reduced infrastructure footprint

### Installation Experience
- [ ] Installer/setup wizard for first-time deployment
- [ ] Docker Compose production configuration
- [ ] Kubernetes Helm chart for self-host
- [ ] Database initialization and migration scripts

### Documentation
- [ ] Self-host installation guide
- [ ] Hardware/infrastructure requirements
- [ ] Backup and recovery procedures
- [ ] Upgrade and migration guides

---

## Key Milestones

| Phase | Target Completion | Status |
|-------|-------------------|--------|
| **Phase 1**: Infrastructure | ✅ Completed | Production-ready foundation |
| **Phase 2**: Core Features | Q2 2025 | In progress (provisioning workflows) |
| **Phase 3**: Frontend Integration | Q3 2025 | Astro sites under development |
| **Phase 4**: SaaS Features | Q4 2025 | Billing + observability |
| **Phase 5**: KiP (Self-Host) | 2026 | Post-MVP release |

**Note**: Timelines are aspirational and subject to change. This is a passion project built to ship iteratively.

---

## Current Focus

### What's Being Built Now (Phase 2)
1. **Agent Enrollment Flow** - mTLS certificate issuance and rotation
2. **Server Provisioning** - Template-based server creation workflows
3. **Node Inventory** - Health tracking and capacity management
4. **Message Consumers** - Command/event handlers for domain logic

### Next Up
1. Real RBAC enforcement in Gateway
2. Organization and team management
3. Integration tests for provisioning workflows
4. Astro frontend API integration (Phase 3 prep)

---

## How to Contribute

This roadmap is a living document. As features are implemented:

1. Move items from "Planned" to the relevant phase's checklist
2. Update completion status (✅ or ❌)
3. Add actual completion dates to milestones
4. Document lessons learned and architectural decisions

For architectural decisions, see [`docs/architecture/`](./architecture/) for detailed design documents.
