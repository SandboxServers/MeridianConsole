# Requirements: Beta — Full Vertical Slice

**Defined:** 2026-07-26
**Core Value:** A user can operate a real game server on their own hardware entirely through the platform

> Replaces the completed "PR #39 Feedback Resolution" requirements (all 11 items
> shipped 2026-01-19).

## v1 (Beta) Requirements

### Auth & Sessions

- [ ] **AUTH-01**: `POST /api/v1/identity/refresh` implemented (JSON contract SharedAuth expects); sessions survive access-token expiry
- [ ] **AUTH-02**: Login UI renders only providers actually configured in BetterAuth
- [ ] **AUTH-03**: Localhost development login works (configurable cookie domain, documented/scripted ES256 keypair setup)
- [ ] **AUTH-04**: ShoppingCart post-login redirect lands on an existing page

### Agent Vertical

- [ ] **AGENT-01**: `/hubs/agent` SignalR hub in Nodes with mTLS-bound connections (ADR-0008)
- [ ] **AGENT-02**: Enrollment request/response contracts unified in `Dhadgar.Contracts`; Gateway route transform fixed; enrollment succeeds end to end
- [ ] **AGENT-03**: Agent bootstrap hosted service (enroll → persist NodeId/OrgId (#101) → connect → dispatch)
- [ ] **AGENT-04**: Command handlers: Ping, StartServer, StopServer, RestartServer, ServerStatus (#118)
- [ ] **AGENT-05**: Command signing implemented control-plane side and verified agent side (#94)
- [ ] **AGENT-06**: Agent-facing APIs and hub methods versioned before first binary ships (#116)
- [ ] **AGENT-07**: Agent.Core security-critical classes have real test coverage

### Server Lifecycle

- [ ] **SRV-01**: PR #88 rebased and merged (Servers, Console, Mods implementations)
- [ ] **SRV-02**: Create-server flow: reserve node capacity → dispatch StartServer → track state from CommandResult/heartbeats
- [ ] **SRV-03**: Services promoted from stub status enforce their own authn/authz
- [ ] **SRV-04**: Minimal console streaming (last-N lines + live tail) via Console service

### Panel & Deployment

- [ ] **UI-01**: Panel `/servers`, `/nodes`, `/settings` pages exist and use the real API client
- [ ] **UI-02**: Dashboard tiles show live counts (servers, nodes, tasks)
- [ ] **DEP-01**: `docker-compose.services.yml` verified end to end; beta install runbook written
- [ ] **DEP-02**: BetterAuth and Panel images built by container CI
- [ ] **TEST-01**: #121 P0 integration tests running in CI

## Out of Scope

| Feature | Reason |
|---------|--------|
| Kubernetes/Helm deploy | Chart non-functional; compose is the beta target |
| Linux agent | Windows-only beta |
| Billing/Tasks features | Parked stubs |
| Files service | Slated for removal (#115) |
| Gaming-provider login UI | Backend exists; UI post-beta |

---
*Requirements defined: 2026-07-26*
