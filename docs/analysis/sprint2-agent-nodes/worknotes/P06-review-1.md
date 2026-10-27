# P06 review, pass 1 (ded8627..7335f07, head 7335f07)

## Verdict

decision

## Fix notes

N1. src/Dhadgar.Gateway/README.md:675 — the authorization-policy table lists `Agent` but not the new `AgentCertificate` policy, so the policy list is stale — add a row directly after the `Agent` row: `| `AgentCertificate` | TLS client certificate whose issuer CN is `AgentCertificateProfile.CaCommonName` (Nodes validates the rest) |` — verify: Grep README.md for `AgentCertificate` and expect a row in the policy table near line 676 as well as the route-table rows.

## Not fixed

1. security-review label needed: Gateway auth path, client-certificate forwarding and `AgentCertificate` policy (packet marks security review: yes).

2. Decision (owner call): `PerAgent` rate-limit bypass and unbounded partitions, `src/Dhadgar.Gateway/Program.cs` (PerAgent policy, about :270-277) together with `AllowAnyClientCertificate()` and `AgentCertificateForwarding.IsAgentIssued`. The spec asks for exactly this code, so the coder is not at fault. The problem is that `IsAgentIssued` only compares the issuer CN string, and Kestrel accepts any certificate. Anyone can mint a self-signed certificate whose issuer CN is `Dhadgar Agent CA` and pass the `AgentCertificate` policy. The limiter runs after authorization (`UseAuthorization` :347, `UseRateLimiter` :350). The partition key is `agent-cert:{sha256}`, so a new throwaway certificate per request gets a fresh 500/min bucket, and the request is proxied to Nodes, which then rejects it after doing chain validation. Net effect: unauthenticated callers can send unlimited requests to Nodes on `/hubs/agent` and `/api/v1/agents/*` and create one limiter partition per certificate. Options for the owner: (a) accept it for beta, because Nodes is the validator and the dev ingress is not public (D-S210 production config is the P15 follow-up); (b) key the limiter by the IP-or-`sub` key when the certificate is not a valid agent-CA leaf, and add a Gateway-side IP limiter in front; (c) have the Gateway validate the chain against the agent CA, which contradicts D-S25. A coder cannot pick between these. A fix of "hash only when `IsAgentIssued`" does not close the hole, because the CN is forgeable.

3. Observation, no change needed: nodes cluster `SessionAffinity` with `SecurePolicy: Always` sets an affinity cookie on all `nodes` traffic, including browser-facing `nodes-org-route`. It is per spec (D-S217). Agent-side `HubConnection` cookie handling is another packet's concern.

4. Observation: `Kestrel ClientCertificateMode` and the `PerAgent` partition key have no test (TestServer bypasses Kestrel, and no test asserts `agent-cert:` keys). The spec did not require one. Gateway tests: 135 passed on re-run, all five new forwarding tests are real. The positive hub case fails without the route, policy, metadata or transform. The spoof cases fail without `AddTransforms`.
