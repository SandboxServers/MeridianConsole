# L00 spike: Windows Server Core containers on this host

Run: 2026-10-27, host `Battlestation`, unelevated PowerShell session. No admin step was run. The spike did not enable any Windows feature, did not reboot, and did not edit Docker Desktop settings files. It did not touch the dev Aspire containers or volumes.

Path note: the packet names the output `C:\Users\Steve\source\projects\MeridianConsole\docs\analysis\meridian-lab\worknotes\L00-windows-containers-spike.md` (the main checkout). This copy sits in the packet worktree, as the brief requires. Ops copies it to the campaign branch.

## Host

| Fact | Value |
| --- | --- |
| Windows build | 10.0.26300 UBR 9711, DisplayVersion 26H2, EditionID ProfessionalWorkstation |
| `Win32_ComputerSystem.HypervisorPresent` | True |
| `Win32_OptionalFeature` InstallState (1 = enabled, 2 = disabled) | VirtualMachinePlatform 1; Microsoft-Hyper-V 2; Microsoft-Hyper-V-All 2; Containers 2; Containers-DisposableClientVM 2 |
| Services | `vmcompute` Running; `vmms` not present |
| WSL | `wsl --status`: default distribution Ubuntu, default version 2 |
| `docker-users` membership | Yes (`Battlestation\docker-users`, enabled) |
| Elevated session | No |
| Docker CLI | 29.1.3; Docker Desktop 4.55.0 (engine 29.1.3) |

Note: `HypervisorPresent` is True while Hyper-V and Containers are disabled. The Linux engine runs on WSL2, which explains it.

## Linux engine

- Docker Desktop was already running. Step 2 did not start it.
- `docker context ls` lists `default`, `desktop-linux` (original current context) and `desktop-windows`, which Docker Desktop created during step 3.
- `docker --context desktop-linux run --rm hello-world`: exit 0, about 1.0 s after the image was present.
- `docker --context desktop-linux info`: OSType linux, kernel `6.6.87.2-microsoft-standard-WSL2`.

## Windows engine

- No `desktop-windows` context existed at the start. `DockerCli.exe -SwitchWindowsEngine` exists (also `-SwitchLinuxEngine`).
- `DockerCli.exe -SwitchWindowsEngine` ran for more than 180 s and then stayed in its switch call. Its log says `Windows containers enabled:false` and `Switching to the Windows engine`.
- While the switch was in flight, `docker --context desktop-linux version` answered `linux 29.1.3`. The Linux engine stayed reachable.
- After the switch, `\\.\pipe\dockerDesktopWindowsEngine` exists, but every call returns `500 Internal Server Error` (`version`, `info`, `ps`). The Windows engine never answered.
- Docker Desktop's `monitor.log` shows `open \\.\pipe\docker_engine_windows: The system cannot find the file specified` on every `_ping`, so the Windows engine process was not running.
- Recovery: `DockerCli.exe -SwitchLinuxEngine` logged `Windows containers enabled:true` and `Switching to the Linux engine`. The Linux engine answered within seconds. Then `docker context use desktop-linux` restored the original current context. Two leftover `DockerCli.exe` switch processes that this spike started were stopped. The background switch task ended with exit -1 after 237 s.
- The `Windows containers enabled` flag read `false` on the first switch and `true` on the second. The source of that flag is unknown. Win32_OptionalFeature still reports `Containers` disabled.

Not run: any step that needs a live Windows engine (step 4 run and step 5 side by side).

## Isolation results

Pulls were checked from the registry, not pulled, because a pull into the Linux engine is useless for Windows images. `docker manifest inspect` runs client-side against the registry.

| Image | process | hyperv | start ms | Result |
| --- | --- | --- | --- | --- |
| `mcr.microsoft.com/windows/servercore:ltsc2025` | not run (no Windows engine) | not run | n/a | Manifest exists: windows/amd64, os.version 10.0.26100.33674. Manifest lookup 1743 ms. |
| `mcr.microsoft.com/windows/nanoserver:ltsc2025` | not run | not run | n/a | Manifest exists: windows/amd64, os.version 10.0.26100.33674. Manifest lookup 1632 ms. |
| `mcr.microsoft.com/dotnet/runtime:10.0-windowsservercore-ltsc2025` | not run | not run | n/a | Manifest exists (single manifest, no platform list). Manifest lookup 1093 ms. |

The expected process-isolation mismatch (host 26300 vs image 26100) could not be observed, because no container ran.

## Side by side

Not tested. The Windows engine did not start, so neither order was run. D-ML38 stays open.

## Inside the container

Not run. Step 6 needs a running Server Core container (step 4). Its two host checks are therefore not recorded: `Cert:\LocalMachine\Root` lookup for `CN=meridian-lab-spike`, and `HKLM:\SOFTWARE\MeridianLabSpike`. Nothing was created on the host.

## Networking

Linux side only (step 7, first half):

- `docker --context desktop-linux run -d --name meridian-lab-spike-web --label meridian-lab=1 -p 127.0.0.1:18999:80 nginx:alpine`: exit 0, 6.2 s including the nginx:alpine pull.
- `docker port`: `80/tcp -> 127.0.0.1:18999`.
- From the host, `http://127.0.0.1:18999/`: HTTP 200.

Windows side (second half): not run. The host has a `vEthernet (WSL ...)` adapter that is Up. Its address (RFC 1918, not recorded here) is the likely NAT gateway for Windows containers, but this is unverified. Whether `host.docker.internal` resolves from a Windows container is unknown. Whether a `127.0.0.1`-only publish is reachable from a Windows container is unknown. The Linux engine has no proof that its loopback publishing reaches a Windows container.

## Limits

Not run: `--storage-opt size=20G`, `--memory 2g`, five simultaneous Server Core containers, `docker stats`. The `docker system df` reading below is the Linux engine only.

`docker --context desktop-linux system df`: Images 75 (74.72 GB, 23.26 GB reclaimable); Containers 26 (14 active); Local volumes 83 (66.13 GB, 7.02 GB reclaimable); Build cache 155 (15.09 GB, 8.08 GB reclaimable). Nothing was pruned.

## Cleanup

- `docker rm -f meridian-lab-spike-web`: removed. No `meridian-lab-spike-*` container is left.
- The image `nginx:alpine` was pulled and is kept, as step 9 says for pulled images.
- Current Docker context: `desktop-linux` (restored). `docker --context desktop-linux ps` works.
- Windows engine: still not running. The `desktop-windows` context and its pipe remain, and answer 500. Docker Desktop created that context; it is not a spike artifact to delete.

## Outcome

**B (provisional).** Only Hyper-V or the `Containers`/`Microsoft-Hyper-V` features could make Windows containers run here, and the packet forbids enabling them in this spike.

Evidence:
- `Containers`, `Microsoft-Hyper-V`, and `Microsoft-Hyper-V-All` are disabled (InstallState 2).
- The Windows engine pipe exists but returns 500 and never answers.
- Docker Desktop's log shows no `docker_engine_windows` process.

Not proven: whether process isolation would run a Server Core image once the features are enabled. That needs the owner's ruling on D-ML37, then a rerun of steps 3 to 9. Outcome C is still possible.

Also recorded: side by side is untested (D-ML38 open). The agent ingress address is untested (C10 NAT publishing is open).

## Recommended `labsettings.json` values

These are provisional. Do not use them for L31 until the Windows engine runs.

- `Lab:Docker:WindowsContext`: `desktop-windows` (Docker Desktop's context name; not validated because the engine does not answer).
- Isolation: unresolved. Test process first, then hyperv. Neither was run.
- Agent-ingress address: unresolved. Candidates are the WSL vEthernet gateway or `host.docker.internal`. Neither was run.
- `Lab:Pool:MaxWindows`: `0` until D-ML37 is ruled and the engine runs.
- Storage-opt (`--storage-opt size=20G`): undecided. Not tested.

## Last check

Checks: this worknote exists, names one outcome (B), and records step 6's two host checks as not run, with the reason. The Linux-side network and cleanup steps passed.
