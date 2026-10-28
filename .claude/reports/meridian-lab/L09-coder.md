# L09 coder report (part 8)

- ServicesDockerTests: CA fetched directly from Nodes (LabPorts.Nodes 15040, /agents/ca-certificate, PEM marker); Gateway /healthz via LabPorts.AgentIngress; POST /api/v1/agents/enroll `{}` via Gateway and AgentIngress expects 400 with `invalid_platform`. Test renamed, summary updated, CaCertificatePath helper removed. Ports verified against LabPorts, GatewayAdapter and NodesAdapter.
- Extra (test-only, strictly needed): `[Collection("LabDockerStack")]` on ServicesDockerTests and LabStackDockerTests. Run in parallel they collided on "could not create the lab network" (reproduced twice); serialised, the full run is green.
- Results: ServicesDockerTests pass alone (47 s) and in the full run. Full lab tests: 249 passed, 4 skipped, 1 failed (accepted: NodesEnrollSeamTests.Enroll_MintedToken_ReturnsCreatedWithNodeId).
- Build: TreatWarningsAsErrors passes. With CI=true the Dhadgar.Lab and Dhadgar.Lab.Probe src projects fail 21 analyzer errors (CA2000, CA1859, CA1822) at the unmodified baseline too (verified by stash); not touched, out of scope.
- No meridian-lab containers or networks left. No product files or packages.lock.json changed. README had no ca-certificate wording.
