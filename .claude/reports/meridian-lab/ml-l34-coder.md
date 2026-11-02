# ml-l34 coder report (fallback implementer)

Commits on ml/l34: eb62d020, a75d6893 (env wiring), 8a63e685 (LabEnvValue, Identity/BetterAuth/Panel adapters), 6e0d22f2 (LabBuilder, LabDaemon registrations), e2f1d4d1 (GatewayAdapter: gateway-identity/gateway-betterauth proxies, identity and betterauth clusters, Readyz__RequiredClusters__1=identity), 293caeb8 (tests, README Services section) plus the worknote/report commit.

Built: node-app.Dockerfile, per-stack ES256 exchange key and BetterAuth secret in the vault, the three adapters, Node build path, registrations, Gateway additions, README Services section.
Tests authored: AuthStackAdapterTests (non-Docker, 7 tests), AuthStackDockerTests and PanelLoginSeamTests (S-L9), both RequiresDocker and RequiresNetwork, not run.

Checks: Dhadgar.Lab and Dhadgar.Lab.Tests Release with TreatWarningsAsErrors build clean. Non-Docker test run: 1496 passed, 5 skipped, 1 failed.
The one failure, GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster, is not caused by L34: JsonDocument.Parse fails on the // comments in src/Dhadgar.Gateway/appsettings.json (added by the HF-02/HF-01 commits on the base). Fix is JsonDocumentOptions { CommentHandling = Skip } in that test (outside the packet files; left for a fix pass).

Deviations: BetterAuth gets the private PEM with line breaks removed (jose importPKCS8 cannot read literal \n); Identity gets the public key with \n escapes. I2OutboxInvariant unchanged: Identity has no outbox model (packet says "if its model has them"). stack_reset_data needs no change: tables are listed dynamically and identity/betterauth are already lab DBs.
