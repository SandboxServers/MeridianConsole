# P03 review, pass 3 (ded8627..562313a, head 562313a)

## Verdict

ship

## Fix notes

None.

Earlier notes checked:

- security-2 N3 applied. `ForwardedClientCertificateMiddleware.cs` no longer writes `Connection.ClientCertificate`. It sets a per-request `ITlsConnectionFeature` through `context.Features.Set` (line 109), keeps `RegisterForDispose`, and the `finally` and `accepted` bookkeeping are gone. `MtlsMiddleware.cs:84` reads `context.Connection.GetClientCertificateAsync()`, which resolves the request-level feature. No other source reader of `Connection.ClientCertificate` exists in `src/Dhadgar.Nodes`.
- The two deviations in `P03-fix-2.md` hold. Reading `context.Features.Get<ITlsConnectionFeature>()` for the own-certificate check avoids filling the `ConnectionInfo` cache. The rewritten sequential test uses two contexts sharing a connection feature, which is the correct model.
- `InvokeAsync_ConcurrentStreamsOnOneConnection_EachNextReadsItsOwnCertificate` gates both streams in flight at `next`, checks each reads its own thumbprint, and checks the shared connection feature stays null. It fails on the old connection-write code (the fix report records 1 failed on old code).
- Local run of the Mtls and Forwarded tests: 31 passed, 0 failed.
- Earlier passes (`certificate_required`, certificate clearing, /0 CIDR) were already confirmed in review-2 and the code is unchanged.

## Not fixed

- security-review label needed: auth (mTLS middleware, forwarded client certificate trust, `AgentMtls` scheme). Applies to the PR for `s2/p03-nodes-mtls`.
- No Kestrel HTTPS h2 two-stream integration test exists (security-2 verify (c) was conditional on a harness). Accepted: the unit test covers the shared-connection feature behaviour; a real TLS test belongs with the P06 seam test.
- Advisory items carried over from pass 1 and 2, out of scope for this packet: trust-model breadth of `TrustedProxies` (D-S210); P06 must strip client-supplied `X-Client-Cert` and set it only from a handshake-validated certificate; P04 must put `[Authorize(Policy = "AgentPolicy")]` on the hub and take `NodeIdClaim` only from the validated claim; `AddAuthentication(AgentMtls)` sets the service `DefaultScheme`; the pre-existing CA validation does not check node status or require an `AgentCertificates` row.
