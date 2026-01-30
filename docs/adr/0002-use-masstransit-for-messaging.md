# ADR-0002: Use MassTransit for Async Messaging

## Status

Accepted

## Context

Microservices need to communicate asynchronously for:
- Event-driven workflows (agent enrolled, server started, etc.)
- Background job processing (file transfers, mod installations)
- Saga orchestration (multi-step provisioning workflows)
- Decoupling services from synchronous dependencies

Options considered:
1. **Raw RabbitMQ client** - Maximum control, significant boilerplate
2. **MassTransit** - Abstraction over message transports with sagas, retries, outbox
3. **NServiceBus** - Commercial alternative with similar features
4. **Wolverine** - Newer, lighter-weight option
5. **Dapr** - Sidecar-based, adds operational complexity

## Decision

Use MassTransit as our messaging abstraction layer with RabbitMQ as the transport.

Configuration patterns:
- `StaticEntityNameFormatter` with `meridian.` prefix for queue/exchange names
- Consumer-per-message-type registration
- Outbox pattern for transactional messaging with EF Core
- Retry policies with exponential backoff

## Consequences

### Positive

- Battle-tested library with excellent documentation
- Built-in saga state machines for complex workflows
- Outbox pattern ensures exactly-once message publishing
- Transport-agnostic (can switch to Azure Service Bus, Amazon SQS)
- Strong typing with message contracts in `Dhadgar.Contracts`

### Negative

- Learning curve for saga development
- Abstraction adds indirection when debugging
- Must follow MassTransit conventions for queue naming

### Neutral

- RabbitMQ is the default transport; production may use managed alternatives
- Message versioning requires careful contract design
