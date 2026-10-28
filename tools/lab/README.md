# meridian-lab install scripts

The lab daemon (`src\Tools\Dhadgar.Lab`, executable `meridian-lab.exe`) is a loopback-only process that Claude Code reaches over MCP at `http://127.0.0.1:8789/mcp`. These scripts install it for the current user. Dev and lab only.

## Files

| File | Purpose |
| --- | --- |
| `install.ps1` | Publishes the daemon to `%LOCALAPPDATA%\meridian-lab\bin`, writes `Lab:RepoRoot` into the published `labsettings.json`, creates `MERIDIAN_LAB_TOKEN` in the user environment when it is empty, then calls `daemon.ps1 -Install`. |
| `daemon.ps1` | Manages the logon task `MeridianLabDaemon`: `-Install`, `-Uninstall`, `-Start`, `-Stop`, `-Status`. |
| `../../.mcp.json` | The Claude Code client entry for `meridian-lab` (`type` http, the loopback URL, the bearer header from the environment). |

## Install

Run from the repository root in PowerShell 7:

```powershell
.\tools\lab\install.ps1
```

`install.ps1` may run elevated, but the task it registers runs as your logged-on user with the Limited run level, so the daemon never runs elevated. The token is written to your user environment; the script never prints it.

When it finishes, restart your terminal so Claude Code sees `MERIDIAN_LAB_TOKEN`.

## Start, stop and status

```powershell
.\tools\lab\daemon.ps1 -Start
.\tools\lab\daemon.ps1 -Stop
.\tools\lab\daemon.ps1 -Status    # prints "HTTP 200" and the status body, or the refusal
```

The task restarts the daemon up to 3 times, one minute apart, after a failure. It has no execution time limit and starts at each logon.

## Uninstall

```powershell
.\tools\lab\daemon.ps1 -Uninstall
```

This removes the task only. The published binaries and the token stay until you delete them.

## Token rotation

```powershell
$bin = Join-Path $env:LOCALAPPDATA 'meridian-lab\bin\meridian-lab.exe'
$new = & $bin token new
[Environment]::SetEnvironmentVariable('MERIDIAN_LAB_TOKEN', $new, 'User')
$new = $null
.\tools\lab\daemon.ps1 -Stop
.\tools\lab\daemon.ps1 -Start
```

Then restart your terminal. Task Scheduler gives the task the user environment captured at logon, so if `-Status` still answers 401 after the restart, sign out and back in.

## Safe modes

Every script that changes the machine accepts `-Preflight`. It prints the plan and changes nothing:

```powershell
.\tools\lab\install.ps1 -Preflight
.\tools\lab\daemon.ps1 -Install -Preflight
.\tools\lab\daemon.ps1 -Status
```

`-Status` only reads, so it runs without `-Preflight`. Both scripts refuse to run unless they sit in `tools\lab` inside the Meridian repository.
