# ADR-0007: Agent Security Model

## Status

Accepted

## Context

Agents run on customer hardware with elevated privileges to manage game servers. This creates a significant trust boundary:

- Agents have local administrator/root access
- Agents communicate with the control plane over the internet
- Compromise of an agent could affect customer infrastructure
- Compromise of the control plane could affect all connected agents

Security requirements:
- Mutual authentication (control plane and agent verify each other)
- Encrypted communication
- Minimal attack surface on agent
- Audit trail of all commands
- Ability to revoke compromised agents

## Decision

Implement a defense-in-depth security model:

### Authentication
- **mTLS (mutual TLS)** for all agent-to-control-plane communication
- Agents receive certificates during enrollment
- Certificates contain `node_id` claim for identity verification
- Certificate Authority (CA) managed by Nodes service

### Enrollment
- One-time enrollment tokens (SHA-256 hashed, stored securely)
- Tokens expire after single use or timeout
- Organization-scoped enrollment (agents belong to orgs)

### Certificate Lifecycle
- 90-day certificate validity
- Automatic renewal before expiration
- Revocation list for compromised certificates
- Thumbprint verification on renewal requests

### Communication Pattern
- Agents initiate all connections (outbound only)
- No inbound ports required on customer firewall
- Heartbeat-based health reporting
- Command queue polling (not push)

### Command Execution
- All commands logged with correlation IDs
- Commands signed by control plane
- Agents verify command authenticity before execution
- Results reported back asynchronously

## Consequences

### Positive

- Strong mutual authentication via mTLS
- No inbound firewall rules required for customers
- Clear audit trail of all operations
- Compromised agent can be revoked centrally
- Certificate rotation limits exposure window

### Negative

- Certificate management complexity
- Enrollment requires secure token distribution
- Clock skew can cause certificate validation issues
- Must handle certificate renewal gracefully

### Neutral

- Agent code is security-critical and requires careful review
- `.claude/agents/agent-service-guardian` enforces review for agent changes
- Agents are open-source for customer inspection
