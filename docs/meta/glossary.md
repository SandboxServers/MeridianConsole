---
title: Glossary
doc_id: meta.glossary
type: reference
audience: [engineers, operators, contributors, agents, new-joiners]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [meta.conventions, arch.overview]
---

# Glossary

The words this repo uses, and what they mean here. Use these terms in docs, issue titles, test names and commit messages. When you add a term to a doc that isn't here, add it here in the same change.

Seeded from CLAUDE.md, [PROJECT-STATE.md](../PROJECT-STATE.md), the ADRs and the sprint 2 campaign. Entries marked *(verify)* need a person to confirm them against the code.

## Platform

| Term | Meaning |
| --- | --- |
| Meridian Console | The product: a control plane for running game servers on customers' own hardware. Code and namespaces use the name **Dhadgar**. |
| Control plane | The set of services the platform runs (Gateway, Identity, Nodes, Secrets and the rest). Agents connect *to* it; it never connects to them. |
| Gateway | `src/Dhadgar.Gateway`. The single public entry point: a YARP reverse proxy with rate limiting, a circuit breaker, Cloudflare IP handling and security headers. See [ADR-0003](../adr/0003-use-yarp-for-api-gateway.md). |
| Cluster (YARP) | A named group of backend addresses in the Gateway's `ReverseProxy:Clusters` config, usually one per service. |
| Route prefix | The `/api/v1` the Gateway exposes and strips before forwarding. A missing strip was DV-1. |
| Service | One control-plane microservice under `src/Dhadgar.<Name>`, owning one domain boundary ([ADR-0005](../adr/0005-microservices-per-domain.md)). |
| Stub service | A service with a project and a README but little or no behaviour yet (Servers, Tasks, Files, Console, Mods, Billing). |
| Organization | The tenant. Users, nodes and servers belong to an organization; most data is scoped by organization ID. |
| KiP | The self-hosted edition of the platform. Not yet written; see `docs/kip/`. *(verify the expansion)* |
| Scope | `src/Dhadgar.Scope`, the Astro documentation and internal-information site. |
| Panel | `src/Dhadgar.Panel`, the Astro/React customer dashboard. |
| ShoppingCart | `src/Dhadgar.ShoppingCart`, the Astro storefront deployed as an Azure Static Web App. |
| SharedAuth | `src/Dhadgar.SharedAuth`, the TypeScript auth client the frontends use. |
| CLI | `src/Dhadgar.Cli`, the `dhadgar` .NET global tool, built on Spectre.Console. |
| AppHost | `src/Dhadgar.AppHost`, the .NET Aspire host for local development. It can't run the whole platform yet (DV-6). |
| Compose stack | `deploy/compose/`. The beta deployment target and the only artefact that runs the full system today. |

## Agents and nodes

| Term | Meaning |
| --- | --- |
| Agent | The program a customer installs on their machine. It runs with high privilege and makes only outbound connections to the control plane. Windows: `Agent.Windows`; Linux: `Agent.Linux` (stub). Not to be confused with a Claude agent. |
| Agent.Core | `src/Agents/Dhadgar.Agent.Core`, the platform-neutral agent library: enrollment, communication, commands, files, process, health, telemetry. |
| GameServerWrapper | `src/Agents/Dhadgar.Agent.GameServerWrapper`, the per-server process host the agent starts for each game server. |
| Node | A customer machine that runs an agent, as the Nodes service records it. |
| Nodes service | `src/Dhadgar.Nodes`. Owns node inventory, enrollment, the agent CA, mTLS, heartbeats and capacity reservations. |
| Enrollment | The one-time exchange in which an agent presents an enrollment token and its public key, and Nodes returns a signed client certificate, the organization ID and the CA certificate. |
| Enrollment token | A short-lived secret an organization admin creates so one agent can enroll. |
| Agent CA | The private certificate authority in Nodes that signs agent client certificates. On Windows its certificate lives in the `MeridianConsoleAgent` machine store, never in `LocalMachine\Root` (D-S219). |
| mTLS | Mutual TLS: both sides present certificates. Agents authenticate to the control plane with their client certificate. |
| SPIFFE ID | The URI in an agent certificate's subject alternative name that identifies the node. Defined in `AgentCertificateProfile` (D-S21). |
| `node_id` claim | The node identity taken from the client certificate. It is the only source of an agent's identity on the hub (D-S23). |
| Agent hub | The SignalR hub at `/hubs/agent` in Nodes that agents hold open for heartbeats and commands ([ADR-0008](../adr/0008-agent-transport-signalr.md), D-S22). Planned in sprint 2. |
| Heartbeat | The agent's periodic status report. It moves to the hub in sprint 2; the HTTP endpoint stays during migration (D-S28). |
| `X-Agent-Protocol` | The header that carries the agent hub protocol version, currently `1` (D-S22). |
| CommandEnvelope | `Agent.Core/Commands/CommandEnvelope.cs`. The wrapper around every command the control plane sends an agent. A security-review area. |
| CommandValidator | Validates a `CommandEnvelope` before dispatch. Signature verification is not implemented and fails closed (#94). |
| IControlPlaneClient | The agent's client interface to the control plane (`Agent.Core/Communication/`). A security-review area. |
| Job Object | The Windows kernel object `WindowsProcessManager` uses to limit and contain game-server processes. |
| Renewal grace | The short window in which an agent's old certificate stays usable after renewal (D-S212). |

## Identity and auth

| Term | Meaning |
| --- | --- |
| Identity service | `src/Dhadgar.Identity`. Users, organizations, role-based access control, OpenIddict, token exchange and refresh, webhooks. |
| BetterAuth | `src/Dhadgar.BetterAuth`, a Node/Express service that handles social OAuth login and issues ES256 exchange tokens. |
| Exchange token | The short-lived token BetterAuth issues after login, which Identity exchanges for platform tokens. |
| Refresh token family | The chain of refresh tokens issued from one login. Reusing an old one revokes the whole family (DV-3). |
| Admin API key | The key that authenticates calls to admin endpoints (`ServiceDefaults/Security/AdminApiKeyAuthenticationExtensions.cs`). |
| problem+json | RFC 7807 error responses (`application/problem+json`). The platform's standard error shape (`ServiceDefaults/Problems/`). |

## Engineering rules

| Term | Meaning |
| --- | --- |
| `Result<T>` | `src/Shared/Dhadgar.Shared/Results/Result.cs`. The return type for operations that can fail. Validation, IO and network failures return a failed result instead of throwing. |
| PathValidator | `Agent.Core/Files/PathValidator.cs`. Every file operation validates its path through it. |
| Trusted host | A host on the allowlist an outbound request must match before it is sent (SSRF protection). There is no central class yet; checks are inline. |
| `ArgumentList` | `ProcessStartInfo.ArgumentList`. The only allowed way to pass arguments to a process; command strings are never concatenated. |
| Central Package Management (CPM) | All NuGet versions live in `Directory.Packages.props`; `.csproj` files never carry versions. |
| ServiceDefaults | `src/Shared/Dhadgar.ServiceDefaults`. The shared middleware every service uses: errors, tracing, logging, security, multi-tenancy, caching, resilience, OpenAPI. |
| Security-review area | Code whose changes need a security review: `IControlPlaneClient`, `CommandEnvelope`, the process managers and the agent projects. |

## How we work

| Term | Meaning |
| --- | --- |
| Contract | The exact shared types, routes, status codes and error shapes two components build against. Cross-boundary types live only in `src/Shared/Dhadgar.Contracts`. |
| Seam | A boundary between two components that were built separately: service to service, or service to agent. |
| Seam test | A test that runs both sides of a seam together (through the Gateway when the real path goes through it) and fails while they disagree. |
| Code exists | The code is written, but nothing proves it works end to end. |
| Confirmed working | A passing seam test or a recorded manual check proves it. |
| Divergence (`DV-n`) | An entry in PROJECT-STATE's divergence register: a place where the code, the docs and the design disagree. |
| Decision (`D1`, `D-S22`) | A recorded owner decision. `D1`–`D4` are in PROJECT-STATE; `D-<campaign><n>` are in a campaign ledger. |
| Campaign | A multi-PR effort run under the orchestrator, with a ledger in `docs/analysis/<campaign>/`. |
| Packet | One prescriptive unit of campaign work, sized for one `packet-coder` session. |
| Wave | A group of about six packets reviewed together. |
| Ledger | A campaign's `README.md`, `work-packets.md` and `RESUME.md`. |
