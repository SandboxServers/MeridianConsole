# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for the Meridian Console platform.

ADRs document significant architectural decisions, their context, and consequences. They serve as a historical record of why the system is built the way it is.

## Index

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-record-architecture-decisions.md) | Record Architecture Decisions | Accepted |
| [0002](0002-use-masstransit-for-messaging.md) | Use MassTransit for Async Messaging | Accepted |
| [0003](0003-use-yarp-for-api-gateway.md) | Use YARP for API Gateway | Accepted |
| [0004](0004-first-party-baseline-philosophy.md) | First-Party Baseline Philosophy | Accepted |
| [0005](0005-microservices-per-domain.md) | Microservices per Domain Boundary | Accepted |
| [0006](0006-postgresql-as-primary-database.md) | PostgreSQL as Primary Database | Accepted |
| [0007](0007-agent-security-model.md) | Agent Security Model | Accepted |

## Creating a New ADR

1. Copy `TEMPLATE.md` to a new file: `XXXX-short-title.md`
2. Fill in the sections with your proposal
3. Submit as part of a PR for review
4. Once merged, the ADR is considered "Accepted"

## ADR Lifecycle

- **Proposed**: Under discussion, not yet implemented
- **Accepted**: Decision made and implemented
- **Deprecated**: No longer relevant but kept for historical reference
- **Superseded**: Replaced by a newer ADR (link to replacement)

## Further Reading

- [Michael Nygard's original ADR article](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [ADR GitHub organization](https://adr.github.io/)
