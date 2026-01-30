# ADR-0004: First-Party Baseline Philosophy

## Status

Accepted

## Context

Building a platform requires choosing between:
- First-party Microsoft/.NET ecosystem tools
- Third-party libraries and frameworks
- Custom implementations

Each choice has trade-offs in terms of support, maintenance, learning curve, and flexibility.

## Decision

Adopt a **first-party baseline philosophy**: prefer Microsoft/.NET ecosystem tools unless there's a compelling reason to choose otherwise.

This means:
- **ASP.NET Core** for HTTP APIs (not alternative frameworks)
- **Entity Framework Core** for data access (not Dapper, raw ADO.NET)
- **OpenTelemetry** for observability (Microsoft-supported)
- **YARP** for reverse proxy (Microsoft project)
- **Azure services** for cloud infrastructure when deploying to cloud

Exceptions are made when first-party options are significantly inferior:
- **MassTransit** over raw Azure Service Bus client (see ADR-0002)
- **PostgreSQL** over SQL Server (see ADR-0006)
- **Better Auth** for passwordless authentication

## Consequences

### Positive

- Consistent patterns across the codebase
- Lower learning curve for .NET developers
- Long-term support from Microsoft
- Better integration between components
- Easier to find documentation and community help

### Negative

- May miss innovations from third-party ecosystem
- Microsoft tools sometimes lag behind alternatives
- Lock-in to .NET ecosystem (acceptable for this project)

### Neutral

- Exceptions should be documented (as ADRs when significant)
- "First-party" includes well-supported OSS in the .NET ecosystem
