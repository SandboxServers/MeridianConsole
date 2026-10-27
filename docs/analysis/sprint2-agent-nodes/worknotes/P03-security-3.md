# P03 security review, pass 3

Range `ded8627..562313a` (fix commit `d9e878d..562313a`), worktree `sprint2-agent-nodes-p03`.

## Verdict

ship

## Fix notes

Prior notes:

- Pass 2 N3 (forwarded certificate on connection-scoped state): fixed. `ForwardedClientCertificateMiddleware.cs` no longer writes `Connection.ClientCertificate`. It calls `context.Features.Set<ITlsConnectionFeature>(new ForwardedTlsConnectionFeature(certificate))` on the request feature collection, so a second HTTP/2 stream sharing the connection cannot read it, and no `finally` clear can null another stream's certificate. `RegisterForDispose` is kept. `MtlsMiddleware.cs:84` reads `context.Connection.GetClientCertificateAsync()`, which resolves the request feature (`ConnectionInfo` re-fetches when the feature collection revision changes after `Set`).
- The own-certificate check now reads `context.Features.Get<ITlsConnectionFeature>()?.ClientCertificate` before anything is set. A real handshake certificate still wins over the header. Nothing else writes to the feature, so it only ever holds a handshake certificate at that point.
- Pass 1 N1 and N2: still fixed. The `/0` rejection in `MtlsMiddleware.cs` and the `/0` skip in the middleware are untouched in this range.
- Tests: `InvokeAsync_ConcurrentStreamsOnOneConnection_EachNextReadsItsOwnCertificate` gates both streams in `next` and asserts each reads its own thumbprint and the shared connection feature stays null. This is the concurrent case pass 2 asked for. The sequential test was rewritten with two contexts, as the fix report says.

No new findings. The diff adds no paths, processes, outbound requests, logging of certificate contents, or secrets. The private nested feature class holds the certificate only for the request, and the certificate is disposed with the response.

Residual risk, not a defect in the diff: the tests use `DefaultHttpContext`. Under real Kestrel h2 I could not run a harness (none exists in the Nodes test project; the fix report says the same). From reading, `Features.Set` on a Kestrel stream goes to that stream's feature collection and not to the connection's, so the property holds. P07 seam coverage with two simultaneous h2 streams carrying different `X-Client-Cert` values would confirm it.

## Not fixed

Carried over, still advisory (see `P03-security-1.md` and `P03-security-2.md`): trust-model breadth of `TrustedProxies` (D-S210); P06 must strip client-supplied `X-Client-Cert` and set it only from a handshake-validated certificate; P04 must put `[Authorize(Policy = "AgentPolicy")]` on the hub and take `NodeIdClaim` only from the validated claim (`RequireClientCertificate=false` and `Mtls:Enabled=false` let `/hubs/agent` pass with no principal); `AddAuthentication(AgentMtls)` sets the service `DefaultScheme`; pre-existing CA validation accepts CA-signed certificates with no `AgentCertificates` row and does not check node status; no Kestrel h2 integration test for concurrent forwarded streams.

security-review label needed
