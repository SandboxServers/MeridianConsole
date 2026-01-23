# Agent Core Library

Shared logic for customer-hosted agents.

## Tech Stack
- .NET Class Library

## Contents
- Agent communication protocols
- Command execution framework
- Health reporting

## SECURITY CRITICAL
Agents run on customer hardware with elevated privileges. All code changes must be reviewed for:
- Command injection vulnerabilities
- Path traversal attacks
- Privilege escalation
- Resource exhaustion

## Notes
- Agents make outbound-only connections (no inbound firewall holes)
- mTLS for all communication (planned)
