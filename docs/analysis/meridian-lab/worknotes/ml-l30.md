# L30 worknote (machine pool)

Worktree: `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l30`, branch `ml/l30`. Finished by the fallback implementer.

## Done (committed)

- Part 1 `4ecc0d79`: FaultTargetGuard member branch and BlastRadius member refusals.
- Part 2 `9968f1db`: pool model (`Pool/PoolTypes.cs`, `PoolManager.cs`), `LinuxContainerProvider.cs`, `HostProvider.cs`, `VmProvider.cs`, `WindowsContainerProvider.cs` stub.
- Part 3 `7052442a`: `Mcp/Areas/PoolArea.cs`, `Tools/Pool/PoolTools.cs`, `Policy/ToolPolicy.cs`.
- Part 4 `86d65a7e`: `tools/lab/images/linux-member.Dockerfile`, `LabBuilder` member image, I5 `orphan-member` check (optional delegates).
- Part 5 `83fd1412`: README "Pool" section.
- `52cc68f4`: UatRunner `requires_pool` (acquire once per section, `${member}`, `member_reset` before each row unless `reuse_member`, release in `finally`, `pool: <error>` skips, `run.json.members`); I5 delegates wired in `InvariantsArea`; `PoolManager.MemberContainerNames()`.
- `4afc8654`: `member_reset` and `pool_release` implement `IFaultTool` and go through `FaultTargetGuard`; tests in `tests/Dhadgar.Lab.Tests/Pool/` (PoolManagerTests, HostProviderTests, RunnerPoolTests, LinuxPoolDockerTests, PoolTestSupport).

## Deviations

- `StartAsync` keeps `pool.json` when a listed container is already gone, because the guard's inspect fails.
- `IPoolProvider` gains `PoolDimensions Dimensions { get; }`.
- The guard matches the exact container name plus labels, not the stored container id (a reset recreates the container).
- I5 member check uses delegates; wiring lives in `InvariantsArea`, outside the packet file list.
- `member_reset` and `pool_release` are `IFaultTool`s resolved through the guard; the `member` argument accepts a bare id or `m:<id>`.
- The runner never resets or releases the `host` member (the guard refuses `m:host`).
- `LinuxPoolDockerTests` is authored, trait `RequiresDocker=true`, not run.

## Pre-existing failure, not fixed

`GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster` parses `src/Dhadgar.Gateway/appsettings.json` with strict `JsonDocument.Parse`; that file has `//` comment lines (last touched by `fddf0f7c`, not by this branch; the test and file are unchanged against base 8191eb8f). Left as is.

## Last check

See the report for the filtered run result line.
