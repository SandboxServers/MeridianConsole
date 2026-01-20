# External Integrations

**Analysis Date:** 2025-01-19

## APIs & External Services

**Authentication Providers:**
- Steam OpenID - Gaming identity
  - SDK/Client: `AspNet.Security.OpenId.Steam`
  - Auth: `OAuth:Steam:ApplicationKey` in `src/Dhadgar.Identity/appsettings.json`
- Battle.net OAuth - Gaming identity
  - SDK/Client: `AspNet.Security.OAuth.BattleNet`
  - Auth: `OAuth:BattleNet:ClientId`, `OAuth:BattleNet:ClientSecret`
- Microsoft Account - Enterprise identity
  - SDK/Client: `Microsoft.AspNetCore.Authentication.MicrosoftAccount`
  - Auth: Standard Microsoft OAuth flow

**Discord:**
- Discord Bot API - Server notifications, slash commands
  - SDK/Client: `Discord.Net 3.13.0` in `src/Dhadgar.Discord/`
  - Auth: `Discord:BotToken`, `Discord:WebhookUrl`
  - Config: `src/Dhadgar.Discord/appsettings.json`

**Email:**
- SendGrid - Transactional email delivery
  - SDK/Client: `SendGrid 9.29.3` in `src/Dhadgar.Notifications/`
  - Auth: Environment variable (API key)
- Office 365 / Microsoft Graph - Enterprise email
  - SDK/Client: `Microsoft.Graph 5.78.0`
  - Auth: `Azure.Identity` for app registration
  - Implementation: `src/Dhadgar.Notifications/Services/Office365EmailProvider.cs`

**New Relic:**
- APM and distributed tracing
  - Integration: OpenTelemetry OTLP export
  - Endpoint: `https://otlp.nr-data.net:4318`
  - Auth: `NEW_RELIC_LICENSE_KEY` environment variable
  - Config: `deploy/compose/otel-collector-config.yml`

## Data Storage

**Databases:**
- PostgreSQL 16
  - Connection: `ConnectionStrings:Postgres`
  - Client: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0`
  - Services using DB: Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Notifications, Discord, Secrets
  - Local: `localhost:5432` via Docker Compose
  - Default database: `dhadgar_platform`

**File Storage:**
- Local filesystem only (current state)
- Azure Blob Storage planned for agent releases and CLI distribution

**Caching:**
- Redis 7
  - Connection: `Redis:ConnectionString` (e.g., `localhost:6379,password=dhadgar`)
  - Client: `StackExchange.Redis 2.7.27`
  - Used by: Identity service (session/token caching)
  - Local: Port 6379 via Docker Compose

## Authentication & Identity

**Auth Provider:**
- OpenIddict 7.0.0 - OpenID Connect server
  - Implementation: `src/Dhadgar.Identity/`
  - Token storage: PostgreSQL + Redis
- better-auth 1.3.1 - Modern auth library (Node.js)
  - Implementation: `src/Dhadgar.BetterAuth/`
  - Database: PostgreSQL via `pg` driver

**JWT Configuration:**
- Issuer: `https://meridianconsole.com/api/v1/identity`
- Audience: `meridian-api`
- Key source: Azure Key Vault (`Auth:KeyVault:VaultUri`)
- Signing certificate: `identity-signing-cert`
- Encryption certificate: `identity-encryption-cert`

**Token Exchange:**
- BetterAuth issues short-lived tokens
- Identity service exchanges for platform tokens
- Exchange endpoint: `https://meridianconsole.com/api/v1/identity/exchange`

## Monitoring & Observability

**Error Tracking:**
- Not detected (rely on logs/traces)

**Logs:**
- OpenTelemetry OTLP export to Loki
- Grafana for visualization (local: `localhost:3000`)
- Structured logging with correlation IDs

**Metrics:**
- OpenTelemetry export to Prometheus
- Prometheus scraping: `localhost:9090`
- OTLP Collector exposes Prometheus endpoint on port 8889

**Tracing:**
- OpenTelemetry distributed tracing
- Export to New Relic (production)
- Instrumentation: ASP.NET Core, HTTP, Runtime, Process

**Dashboards:**
- Grafana with auto-provisioned datasources
- Config: `deploy/compose/grafana/provisioning/datasources/`

## CI/CD & Deployment

**Hosting:**
- Azure Container Registry (ACR): `meridianconsoleacr-etdvg4cthscffqdf.azurecr.io`
- Azure Static Web Apps (frontend)
- Docker containers for microservices

**CI Pipeline:**
- Azure Pipelines (`azure-pipelines.yml`)
- External template repo: `SandboxServers/Azure-Pipeline-YAML`
- Self-hosted agent pool: "Sandbox Servers Agents"

**Security Scanning:**
- SAST, SCA, Container, IaC, Secrets, SBOM scanning enabled
- NVD API integration for vulnerability data
- SecurityCodeScan.VS2019 for .NET SAST

**DNS:**
- Cloudflare (optional provisioning via pipeline)
- Token: `CLOUDFLARE_API_TOKEN` variable

## Secrets Management

**Azure Key Vault:**
- Production secrets: `https://mc-core.vault.azure.net/` (Identity)
- OAuth secrets: `https://mc-oauth.vault.azure.net/` (Secrets service)
- SDK: `Azure.Security.KeyVault.*` packages
- Authentication: `Azure.Identity` (DefaultAzureCredential)

**Secret Categories:**
- OAuth provider credentials
- BetterAuth JWT secrets
- Infrastructure passwords (DB, Redis, RabbitMQ)
- Signing/encryption certificates

**Implementation:**
- `src/Dhadgar.Secrets/Services/KeyVaultSecretProvider.cs`
- `src/Dhadgar.Secrets/Services/KeyVaultCertificateProvider.cs`
- `src/Dhadgar.Secrets/Services/AzureKeyVaultManager.cs`

## Messaging

**Message Bus:**
- RabbitMQ 3 with management plugin
  - Local: `localhost:5672` (AMQP), `localhost:15672` (Management UI)
  - Auth: `RabbitMq:Username`, `RabbitMq:Password`
- MassTransit 8.3.6 abstraction
  - Config: `src/Shared/Dhadgar.Messaging/`
  - All microservices include MassTransit references

**Message Patterns:**
- Publish/subscribe (events)
- Request/response (commands)
- Sagas (planned, not implemented)

## Webhooks & Callbacks

**Incoming:**
- Discord interactions endpoint (planned)
- OAuth callbacks: `/api/v1/identity/callback/*`

**Outgoing:**
- Discord webhooks: `Discord:WebhookUrl` for notifications
- Implementation: `src/Dhadgar.Discord/`

## Environment Configuration

**Required env vars (production):**
- `ConnectionStrings__Postgres` - Database connection
- `RabbitMq__Host`, `RabbitMq__Username`, `RabbitMq__Password` - Message bus
- `Redis__ConnectionString` - Cache
- `Auth__SigningKey` or Azure Key Vault credentials
- `Discord__BotToken` - Discord integration
- `NEW_RELIC_LICENSE_KEY` - Observability (optional)
- `AZURE_TENANT_ID` - Azure authentication

**Secrets location:**
- Development: `dotnet user-secrets` per project
- Production: Azure Key Vault + environment variables

## API Gateway (YARP)

**Configuration:** `src/Dhadgar.Gateway/appsettings.json`

**Backend Services:**
| Route | Cluster | Port | Auth Policy |
|-------|---------|------|-------------|
| `/api/v1/identity/*` | identity | 5010 | Anonymous |
| `/api/v1/betterauth/*` | betterauth | 5130 | Anonymous |
| `/api/v1/billing/*` | billing | 5020 | TenantScoped |
| `/api/v1/servers/*` | servers | 5030 | TenantScoped |
| `/api/v1/nodes/*` | nodes | 5040 | TenantScoped |
| `/api/v1/tasks/*` | tasks | 5050 | TenantScoped |
| `/api/v1/files/*` | files | 5060 | TenantScoped |
| `/api/v1/console/*` | console | 5070 | TenantScoped |
| `/api/v1/mods/*` | mods | 5080 | TenantScoped |
| `/api/v1/notifications/*` | notifications | 5090 | TenantScoped |
| `/api/v1/firewall/*` | firewall | 5100 | TenantScoped |
| `/api/v1/secrets/*` | secrets | 5110 | TenantScoped |
| `/api/v1/discord/*` | discord | 5120 | TenantScoped |
| `/api/v1/agents/*` | nodes | 5040 | Agent |
| `/hubs/console/*` | console | 5070 | TenantScoped |

**Rate Limiting Policies:**
- Global: 1000 req/min
- PerTenant: 100 req/min, replenish 50/sec
- PerAgent: 500 req/min
- Auth: 30 req/min

**Health Checks:**
- Active health checks every 30s on `/healthz`
- Passive transport failure monitoring
- Circuit breaker: 5 failures, 30s open duration

## Real-Time Communication

**SignalR:**
- Service: `src/Dhadgar.Console/`
- Hub route: `/hubs/console/*`
- Session affinity enabled (cookie-based)
- Used for: Game server console streaming

## Cloudflare Integration

**Features:**
- Dynamic IP range fetching for trusted proxies
- Refresh interval: 60 minutes
- Config: `Cloudflare:EnableDynamicFetch` in Gateway

---

*Integration audit: 2025-01-19*
