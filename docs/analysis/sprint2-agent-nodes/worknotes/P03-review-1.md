# P03 review, pass 1 (ded8627..2b29de0, head 2b29de0)

## Verdict

fix

## Fix notes

N1. src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs:94 — a request with no client certificate gets errorCode `missing_client_certificate`. Spec C4 (renew: "401 `certificate_required`") and C5 (the `MtlsMiddleware` 401 codes list) say `certificate_required`, and the new `AgentMtls` challenge uses `certificate_required`, so the same condition gives two codes depending on the path. The coder kept the old string. Nothing in src or tests asserts the old string (tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs:702 and :713 only use `DoesNotContain`). — Change `MtlsProblemWriter.WriteUnauthorizedAsync(context, "missing_client_certificate",` to `MtlsProblemWriter.WriteUnauthorizedAsync(context, "certificate_required",`. Also change the comment at tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs:702 and the string at :713 from `missing_client_certificate` to `certificate_required`. — verify: `dotnet test tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~MtlsMiddlewareTests"` passes, and `Grep missing_client_certificate src tests` returns nothing.

N2. tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs:~305 (`InvokeAsync_HubNegotiateWithoutCertificate_Returns401ProblemJson`) — the test checks status, content type and `nextCalled` but not the C5 body, so it would pass with any errorCode (it is how N1 slipped through). — After `Assert.Equal("application/problem+json", context.Response.ContentType);` add:
```csharp
context.Response.Body.Position = 0;
using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
Assert.Equal("certificate_required", body.RootElement.GetProperty("errorCode").GetString());
Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
```
and set `context.Response.Body = new MemoryStream();` right after `CreateHttpContext("/hubs/agent/negotiate")` if `CreateHttpContext` does not already do so (check the helper at the bottom of the file; use its pattern if it does). — verify: the test fails when N1's string is reverted to `missing_client_certificate`.

## Not fixed

- security-review label needed: auth (mTLS middleware, forwarded client certificate trust, authentication scheme). Applies to the PR for `s2/p03-nodes-mtls`.
- Coder deviation, appsettings `Mtls` block added at top level because the file had no `Mtls` section: correct, no change needed (confirmed the diff adds a new top-level section only).
- Coder deviation, the packet's "revert check" for the scheme (no scheme gives `InvalidOperationException`) is not an automated test: the packet lists it as a revert check, not a required test, and both scheme tests fail without the scheme. Accepted.
- Startup guard tests call `AddMtlsAuthentication` directly rather than starting a host: that matches the guard's location and the "follow the Enabled=false guard pattern" instruction. Accepted.
- The spec requires a test that `/agents/enroll` stays exempt: it is already covered by existing `InvokeAsync_ExemptEnrollPath_PassesThrough` and `InvokeAsync_ExemptCaCertificatePath_PassesThrough`, and the code path is unchanged. Accepted.
- Checked and clean: C6 conditions (enabled, trusted CIDR after IPv4-mapped normalisation, exactly one value, length cap, no existing cert, base64 DER), header removed in every case, only `FormatException`/`CryptographicException` caught, `RegisterForDispose` used, forwarded middleware registered before `MtlsMiddleware`, `Program.cs` untouched, no `Version=` in csproj, header and hub path taken from Contracts. The 26 Mtls/Forwarded tests pass locally.
