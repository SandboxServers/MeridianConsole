# L30 coder report (machine pool)

Status: done (fallback implementer, after three packet-coder continuations).

## Commits (branch ml/l30)
- 4ecc0d79 member fault target guard and blast-radius refusals
- 9968f1db machine pool with linux container and host providers
- 7052442a pool MCP area and pool tools
- 86d65a7e linux member image, lab_build member target, I5 member orphans
- 83fd1412 pool section in the lab README
- 52cc68f4 UatRunner requires_pool, I5 delegates wired in InvariantsArea
- 4afc8654 pool tests; member_reset and pool_release through FaultTargetGuard

## Built in this session
- `member_reset` and `pool_release` implement `IFaultTool` and resolve `m:<id>` through `FaultTargetGuard` ("reset"); the guard is not weakened.
- UatRunner `requires_pool`: one acquire per section, `${member}`, reset before each row unless `reuse_member`, release in `finally`, pool errors skip rows with `pool: <error>`, `run.json.members`.
- I5 `orphan-member` wired in `InvariantsArea`.
- Tests in `tests/Dhadgar.Lab.Tests/Pool/`: PoolManagerTests, HostProviderTests, RunnerPoolTests, LinuxPoolDockerTests (authored, not run), plus BlastRadius coverage.

## Checks
- Build of the test project: 0 errors.
- `dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`: Failed! - Failed: 1, Passed: 1528, Skipped: 5, Total: 1534.
- The one failure is `GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster`: strict `JsonDocument.Parse` on `src/Dhadgar.Gateway/appsettings.json`, which has `//` comments. The test and file are unchanged from base 8191eb8f. Pre-existing; not fixed.

## Deviations
- StartAsync keeps pool.json when a listed container is already gone (guard inspect fails).
- IPoolProvider gains `Dimensions`.
- The guard matches the exact container name plus labels, not the stored container id.
- I5 member check uses delegates wired in InvariantsArea, outside the packet file list.
- `pool_release` and `member_reset` are `IFaultTool`s; `member` accepts a bare id or `m:<id>`.
- The runner never resets or releases the host member.
- LinuxPoolDockerTests authored but not run (Docker).
