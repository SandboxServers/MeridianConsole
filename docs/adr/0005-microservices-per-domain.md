# ADR-0005: Microservices per Domain Boundary

## Status

Accepted

## Context

The platform manages multiple distinct domains:
- User identity and organizations
- Game servers and their lifecycle
- Hardware nodes and agents
- Files and mod distribution
- Billing and subscriptions
- Real-time console access

Architectural options:
1. **Monolith** - Single deployable, shared database
2. **Modular monolith** - Single deployable, internal boundaries
3. **Microservices** - Separate deployables per domain
4. **Serverless functions** - Fine-grained per operation

## Decision

Organize the platform as microservices aligned with domain boundaries:

| Service | Domain |
|---------|--------|
| Identity | Users, organizations, roles |
| Servers | Game server lifecycle |
| Nodes | Hardware inventory, agents |
| Tasks | Background job orchestration |
| Files | File storage and transfer |
| Mods | Mod registry and distribution |
| Billing | Subscriptions, usage metering |
| Notifications | Alerts across channels |
| Console | Real-time server access |
| Secrets | Secure credential storage |

Key constraints:
- Services **cannot** have compile-time dependencies on each other
- Communication via HTTP APIs or message bus only
- Each service owns its database schema
- Shared code lives in libraries (`Contracts`, `Shared`, `ServiceDefaults`)

## Consequences

### Positive

- Independent deployment and scaling per service
- Clear ownership boundaries for teams
- Technology flexibility per service (though we standardize on .NET)
- Fault isolation - one service failing doesn't take down others

### Negative

- Distributed system complexity (network failures, eventual consistency)
- Cross-service transactions require sagas
- More infrastructure to manage (multiple databases, deployments)
- Integration testing is more complex

### Neutral

- Local development uses Docker Compose to run dependencies
- Gateway provides unified API surface to clients
- Shared libraries enforce consistency without coupling
