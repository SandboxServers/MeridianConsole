# P04 security review, pass 3

Range 82f8d74..fbe1a19, focus 3fad62f..fbe1a19 (head fbe1a19004fcc718756a6a082519f978429bc282). Checked against P04-security-2 (ship) and P04-fix-2.

## Verdict

ship

## Fix notes

The last fix commit adds one file, `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubCertificateExpiryTests.cs`. No production code changed, so it cannot introduce a regression. I checked that the tests exercise the real `AgentHub.IsCertificateCurrentAsync` and match its behaviour:

- Expired certificate (NotAfter 1 s in the past): refused, `Abort()` called once, `heartbeat_failed`, and `ProcessHeartbeatAsync` is never reached. Matches `AgentHub.cs` lines 75-80.
- Missing `certificate_not_after` claim: refused and aborted (fail closed). Matches lines 114-119.
- `AllowExpiredCertificates=true` with an expired certificate: acknowledged. Matches the line 112 gate.
- Valid future NotAfter: acknowledged, no abort.
- The hub constructor order in the tests matches the primary constructor. The tests use a `FakeTimeProvider`, an in-memory DB and a substituted `HubCallerContext`. They contain no secrets, certificates or network calls.

This closes the pass 2 "no hub-level expiry test" note. No new findings.

## Not fixed

- The revocation-by-thumbprint branch is covered by the existing integration tests (revoked at connect and revoked after connect), not by these unit tests. That is acceptable.
- Pre-existing, unchanged: `CertificateAuthorityService.ValidateCertificateAsync` accepts any CA-signed certificate with no `AgentCertificates` row, and the SPIFFE node ID is not bound to a stored thumbprint. Decision for the orchestrator, not a P04 change.
- `MtlsMiddleware` does not check revocation, so the handshake succeeds before the hub aborts. Pre-existing.
- A decommissioned node's certificate is not revoked by this packet. The node keeps an open connection but cannot write heartbeats.
- No per-connection heartbeat rate limit. Low risk because the caller is an enrolled node.
- Gateway exposure of `/hubs/agent`, `X-Client-Cert` stripping and the HTTP/2 simultaneous-stream seam test are covered by P03, P06 and P07.

security-review label needed
