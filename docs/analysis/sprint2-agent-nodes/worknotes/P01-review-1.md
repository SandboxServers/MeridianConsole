# P01 review, pass 1

Reviewed head: fea4dc9a989d9e31ab01cfdd88d10cbd2ddbd61d (range 2d8e4d0..fea4dc9)

## Verdict

ship

## Fix notes

None.

## Not fixed

- security-review label needed: `CertificateAuthorityService` (certificate issuance and CA subject) and `MtlsOptions` (mTLS trust domain default) changed. Values are identical to the old constants, so there is no behaviour change, but the CA and the mTLS trust domain are in the security-review areas.
- Out of scope, no action: `src/Dhadgar.Nodes/Services/LocalFileCaStorageProvider.cs:32` still has the literal `"MeridianConsole"` (a storage path segment, not the certificate profile) and `CertificateValidationService.cs:123` has the SPIFFE pattern in a comment. The spec did not ask for either to change.

Checked and found correct:

- C1 and C2 files match the spec text exactly, with no extra members.
- The CA substitutions are value-identical (`{nodeId}` as a Guid formats as `D`, same as `SpiffeId`).
- `AgentHubContractsTests`: the wire-JSON asserts fail on any property rename. Reverting the Contracts types fails the build.
- `IssuedCertificate_MatchesSharedProfile` asserts issuer CN, subject `O=`, SAN SPIFFE URI and the 90-day span, as the spec lists.
- I ran `dotnet test` for Contracts.Tests (5/5 passed) and Nodes.Tests filtered to CertificateAuthority and Mtls (43/43 passed).
