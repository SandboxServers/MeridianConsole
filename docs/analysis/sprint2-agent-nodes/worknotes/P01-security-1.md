# P01 security review, pass 1 (2d8e4d0..fea4dc9, head fea4dc9)

## Verdict

ship

Behaviour-preserving refactor. The values moved into `AgentCertificateProfile` are identical to the removed constants (trust domain `meridianconsole.com`, O=`MeridianConsole`, CA CN `Meridian Console Agent CA`, 90 days). `Guid` interpolated with `:D` matches the old `{0}` default formatting, so the SAN URI, subject CN, issuer and validity of issued certificates are unchanged. No validation, key-generation, CSR or revocation path was touched. `AgentHubContracts.cs` adds records, constants and interfaces only, with no endpoint, no command execution and no new trust of unsigned commands (`ReceiveCommand` carries a string, and the doc comment defers shape and signing to #94).

## Fix notes

None blocking.

## Not fixed

Advisory only, no action needed for this packet:

- `AgentCertificateProfile.cs:10` — `SpiffeNodeIdPrefix` repeats the `meridianconsole.com` literal instead of deriving it from `SpiffeTrustDomain`. The two can drift. The consts are interpolatable, so `$"spiffe://{SpiffeTrustDomain}/nodes/"` works as a `const`. Low risk because the CA only uses `SpiffeId()` and `MtlsOptions` only uses `SpiffeTrustDomain`; the prefix is a latent footgun for the later packets that will parse or compare it.
- `AgentCertificateProfile.ForwardedCertificateHeader = "X-Client-Cert"` (Gateway to Nodes). Nothing consumes it yet. The packet that consumes it must make the Gateway strip or overwrite any client-supplied `X-Client-Cert` and `X-Agent-Protocol`. It must also restrict the Nodes side to Gateway-originated traffic, and re-validate the chain, issuer, SPIFFE trust domain and revocation in Nodes. Otherwise anyone who can reach Nodes, or any client through the Gateway, can claim to be any node. Check this in the packet that wires up the hub (pass-1 item for that packet).
- `AgentHubProtocol.NodeIdClaim` / `IAgentHub.Heartbeat` doc says the node comes from the certificate, never the payload. Hold later packets to it: the hub must set the claim only from the validated SPIFFE ID and authorize each method.
- The new test checks the SAN with a `Contains` over `Format(false)` output. That is fine as a drift test, not as proof of exact SAN content.

security-review label needed
