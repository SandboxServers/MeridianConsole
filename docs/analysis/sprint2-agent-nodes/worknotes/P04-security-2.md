# P04 security review, pass 2

Range 82f8d74..3fad62f. Checked against P04-security-1 and P04-fix-1.

## Verdict

ship

## Fix notes

Prior notes, verified against the code:

- N1 (re-check revocation and expiry on open connections): fixed. `AgentHub.IsCertificateCurrentAsync` runs in `OnConnectedAsync` and at the start of every `Heartbeat`, before the payload is touched. It reads `certificate_thumbprint` (SHA-256 hex from `CertificateValidationService.CalculateSha256Thumbprint`, the same form `CertificateAuthorityService.ValidateCertificateAsync` uses for the `AgentCertificates` lookup) and rejects when a revoked row exists. Expiry uses the new `certificate_not_after` claim (Unix seconds, from `MtlsMiddleware`). It fails closed: a missing or unparsable claim is rejected unless `AllowExpiredCertificates` is set. `certificate.NotAfter` is a local-kind `DateTime`, and `new DateTimeOffset(DateTime)` applies the local offset, so the Unix value is correct. Revoked or expired aborts the connection and returns `heartbeat_failed`.
- N1 case (c), close after a decommissioned ack: accepted as a deviation. Aborting before the ack would drop the error code, and `HeartbeatService` writes nothing for a decommissioned or unknown node, so a decommissioned node gains nothing from the open socket beyond repeated refused calls.
- N2 (control characters): fixed. `AgentHeartbeatValidator` rejects `char.IsControl` in `AgentVersion` and every `HealthIssues` entry. NUL, CR and LF are covered, and there is a unit test plus a hub test for NUL in the version.
- N3 (auth boundary tests): fixed. Tests exist for no certificate (401), foreign CA certificate (401), revoked certificate at connect (refused), revoked after connect (refused, connection not Connected), and two-node isolation.

No new findings. The new claim holds only a timestamp. The hub constructor additions (`NodesDbContext`, `IOptions<MtlsOptions>`) are scoped per hub invocation and the query is `AsNoTracking`.

## Not fixed

- No hub-level test for the expiry path (the shared `FakeTimeProvider` cannot move back). The code path is simple and fails closed, so this is not blocking. A unit test of `IsCertificateCurrentAsync` with a short-lived certificate would close it in P07.
- A revoked certificate is refused in the hub (`OnConnectedAsync`), not at the HTTP layer, so the handshake succeeds before the abort. `MtlsMiddleware` does not check revocation. Harmless for the hub, but any other endpoint that relies only on `MtlsMiddleware` for revocation inherits that gap. Pre-existing.
- Pre-existing, unchanged: `CertificateAuthorityService.ValidateCertificateAsync` accepts any CA-signed certificate with no `AgentCertificates` row, and the SPIFFE node ID is not bound to a stored thumbprint. Decision for the orchestrator, not a P04 change.
- A decommissioned node's certificate is not revoked by this packet. The node keeps an open connection but cannot write heartbeats.
- No per-connection heartbeat rate limit. Low risk because the caller is an enrolled node.
- Gateway exposure of `/hubs/agent`, `X-Client-Cert` stripping, and the HTTP/2 simultaneous-stream seam test are covered by P03, P06 and P07.

security-review label needed
