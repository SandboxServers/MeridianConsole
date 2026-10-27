# P03 review, pass 2 (ded8627..d9e878d, head d9e878d)

## Verdict

ship

## Fix notes

None.

Earlier notes checked:

- review-1 N1 applied: no `missing_client_certificate` remains in any `.cs` file. `MtlsMiddleware` uses `certificate_required`. The `DoesNotContain` string in `MtlsMiddlewareTests` was updated.
- review-1 N2 applied: `InvokeAsync_HubNegotiateWithoutCertificate_Returns401ProblemJson` now asserts `errorCode` = `certificate_required` and `status` = 401. It fails if N1's string is reverted.
- security-1 N1 applied: `ForwardedClientCertificateMiddleware.cs:46-64` clears `Connection.ClientCertificate` in a `finally` when this request set it. `RegisterForDispose` is kept. `InvokeAsync_SameConnectionNextRequest_SeesOnlyItsOwnForwardedCertificate` fails without the `finally`: the second request would find the first certificate on the connection and the "handshake certificate wins" rule would keep it. The success tests read the certificate inside `next`.
- security-1 N2 applied: `MtlsMiddleware.cs:276` rejects `PrefixLength == 0` at startup with a message that does not echo the value. `ForwardedClientCertificateMiddleware.cs:154` skips /0 entries. Tests cover `0.0.0.0/0` and `::/0` at startup, and a /0 middleware test showing 10.42.1.5 is not trusted.
- 27 Mtls, Forwarded and AgentMtls tests pass locally.

## Not fixed

- security-review label needed: auth (mTLS middleware, forwarded client certificate trust, `AgentMtls` scheme). Applies to the PR for `s2/p03-nodes-mtls`.
- The coder used a unit test that reuses one `DefaultHttpContext` for two requests instead of the Kestrel/TestServer shared-connection test. Accepted: it exercises the clearing logic and fails when the `finally` is reverted. A real TLS connection test belongs with the P06 seam test.
- The security advisories from pass 1 stay out of scope for this packet and are unchanged. They are trust-model breadth (D-S210), P06 header handling, P04 hub authorization, the `DefaultScheme` ordering, the pre-existing revocation and node-status binding, and the Gateway-facing listener not requesting client certificates.
