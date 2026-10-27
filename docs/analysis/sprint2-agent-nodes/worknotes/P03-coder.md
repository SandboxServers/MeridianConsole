# P03 coder report: Nodes mTLS for the hub, the forwarded certificate and the AgentMtls scheme

Commit: 2b29de0 (branch s2/p03-nodes-mtls, local only, not pushed)

## Files changed
- src/Dhadgar.Nodes/Auth/MtlsOptions.cs: HubPathPrefix, ForwardedCertificate, class ForwardedCertificateOptions.
- src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs: hub path predicate (StartsWithSegments), exempt check limited to agent paths, 401 writer moved out, AddMtlsAuthentication registers the AgentMtls scheme and validates ForwardedCertificate, UseMtlsAuthentication registers the forwarded middleware first.
- src/Dhadgar.Nodes/Auth/MtlsProblemWriter.cs (new): internal writer for 401 and 403 problem+json.
- src/Dhadgar.Nodes/Auth/ForwardedClientCertificateMiddleware.cs (new): C6 rules.
- src/Dhadgar.Nodes/Auth/AgentMtlsAuthenticationHandler.cs (new): scheme AgentMtls; NoResult, challenge 401 certificate_required, forbid 403 forbidden.
- src/Dhadgar.Nodes/appsettings.json: Mtls.ForwardedCertificate defaults.
- src/Dhadgar.Nodes/README.md: protected paths, forwarded certificate trust rule, AgentMtls scheme.
- tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs: hub negotiate 401 problem+json; /hubs/agentx passes through.
- tests/Dhadgar.Nodes.Tests/Auth/ForwardedClientCertificateMiddlewareTests.cs (new): 8 cases from the packet.
- tests/Dhadgar.Nodes.Tests/Auth/AgentMtlsSchemeTests.cs (new): 403 and 401 problem+json via minimal host; two startup-guard tests.

Program.cs is not changed.

## Checks
1. dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
   Build succeeded. 0 Warning(s) 0 Error(s)
2. dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentHubSeamTests"
   Passed! - Failed: 0, Passed: 405, Skipped: 0, Total: 405

## Deviations
- appsettings.json had no Mtls section, so the block was added at top level: "Mtls": { "ForwardedCertificate": { "Enabled": false, "TrustedProxies": [] } }.
- WriteAsJsonAsync replaces the content type with application/json, so the writer now passes contentType "application/problem+json" explicitly. Existing MtlsMiddleware 401 responses now carry problem+json as C5 requires.
- C5 lists certificate_required as an existing MtlsMiddleware code, but the middleware emits missing_client_certificate for a request with no certificate. I kept the existing code. A hub request without a certificate returns missing_client_certificate; the AgentMtls challenge returns certificate_required.
- The startup guard throws InvalidOperationException from AddMtlsAuthentication, following the Enabled guard pattern, not ValidateOnStart.
- MtlsMiddleware resolves ICertificateValidationService for every request, so the scheme test host registers a substitute for it.
- The packet's "revert check" (without the scheme, both throw InvalidOperationException) is not automated as a test.
- Commit trailer uses the Co-Authored-By line from the brief (Sonnet 5.5).
