#requires -Version 7.0
<#
.SYNOPSIS
Controls the meridian-lab daemon: its logon scheduled task (MeridianLabDaemon) and a read-only status call.

.DESCRIPTION
Exactly one action is required. The task runs meridian-lab.exe daemon run as the logged-on user, at logon,
with the Limited run level, so the daemon never runs elevated. It restarts on failure 3 times, one minute apart,
and has no execution time limit. Token values are read from the environment and never printed.

.PARAMETER Install
Registers the task. install.ps1 publishes meridian-lab.exe first.

.PARAMETER Uninstall
Removes the task.

.PARAMETER Start
Starts the task now.

.PARAMETER Stop
Stops the running task instance.

.PARAMETER Status
Reads GET /status on 127.0.0.1 with the bearer token. Read-only.

.PARAMETER Preflight
Safe mode. Prints what -Install, -Uninstall, -Start or -Stop would change and changes nothing.
-Status is already read-only and runs as usual.
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$Start,
    [switch]$Stop,
    [switch]$Status,
    [switch]$Preflight
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Mirrors LabDefaults.DaemonPort, LabDefaults.StatusPath and LabDefaults.TokenEnvVar in src\Tools\Dhadgar.Lab.
$taskName = 'MeridianLabDaemon'
$daemonPort = 8789
$statusPath = '/status'
$tokenVar = 'MERIDIAN_LAB_TOKEN'

function Exit-Lab {
    param([string]$Message, [int]$Code = 1)
    [Console]::Error.WriteLine($Message)
    exit $Code
}

if (-not $IsWindows) {
    Exit-Lab '[Lab.Unavailable] daemon.ps1 manages a Windows scheduled task' 1
}

$requested = @($Install, $Uninstall, $Start, $Stop, $Status) | Where-Object { $_ }
if (@($requested).Count -ne 1) {
    Exit-Lab '[Lab.InvalidArgument] choose exactly one of -Install, -Uninstall, -Start, -Stop, -Status (-Preflight is an optional safe mode)' 2
}

# R-PATH: refuse to run unless this script sits in <repo>\tools\lab.
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'Dhadgar.sln') -PathType Leaf)) {
    Exit-Lab '[Lab.PathRefused] tools\lab must sit inside the Meridian repository (Dhadgar.sln not found two levels up)' 2
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    Exit-Lab '[Lab.Unavailable] LOCALAPPDATA is not set' 1
}

$binDir = Join-Path $env:LOCALAPPDATA 'meridian-lab\bin'
$exe = Join-Path $binDir 'meridian-lab.exe'
$userId = "$env:USERDOMAIN\$env:USERNAME"

if ($Status) {
    $token = [Environment]::GetEnvironmentVariable($tokenVar, 'Process')
    if ([string]::IsNullOrEmpty($token)) {
        $token = [Environment]::GetEnvironmentVariable($tokenVar, 'User')
    }

    if ([string]::IsNullOrEmpty($token)) {
        Exit-Lab "[Lab.InvalidArgument] $tokenVar is not set; open a new terminal after install.ps1" 2
    }

    $headers = @{ Authorization = "Bearer $token" }
    $token = $null
    try {
        # Loopback only, no redirects (R-HOST). The token value is never written to output.
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$daemonPort$statusPath" -Headers $headers -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 10
    }
    catch {
        $headers = $null
        Exit-Lab "[Lab.Unavailable] the daemon is not reachable on 127.0.0.1:$daemonPort" 1
    }

    $headers = $null
    $code = [int]$response.StatusCode
    Write-Output "HTTP $code"
    if ($code -eq 200) {
        Write-Output $response.Content
    }

    exit 0
}

$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

if ($Install) {
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
    $principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
    $action = New-ScheduledTaskAction -Execute $exe -Argument 'daemon run' -WorkingDirectory $binDir
    $exeReady = Test-Path -LiteralPath $exe -PathType Leaf

    if ($Preflight) {
        Write-Output "preflight: would register scheduled task $taskName"
        Write-Output "preflight:   action    $exe daemon run"
        Write-Output "preflight:   trigger   at logon of $userId"
        Write-Output '              principal Interactive logon, Limited run level'
        Write-Output '              restart   3 times, 1 minute apart; no execution time limit'
        Write-Output "preflight:   binary    $(if ($exeReady) { 'present' } else { 'missing; install.ps1 publishes it first' })"
        Write-Output "preflight:   existing  $(if ($null -ne $task) { 'task present; -Install replaces it' } else { 'none' })"
        Write-Output 'preflight: nothing changed'
        exit 0
    }

    if (-not $exeReady) {
        Exit-Lab "[Lab.NotInstalled] $exe is missing; run tools\lab\install.ps1 first" 1
    }

    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Output "registered scheduled task $taskName for $userId"
    exit 0
}

if ($Uninstall) {
    if ($null -eq $task) {
        Write-Output "scheduled task $taskName is not registered"
        exit 0
    }

    if ($Preflight) {
        Write-Output "preflight: would remove scheduled task $taskName"
        Write-Output 'preflight: nothing changed'
        exit 0
    }

    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Output "removed scheduled task $taskName"
    exit 0
}

# -Start or -Stop
if ($null -eq $task) {
    Exit-Lab "[Lab.NotInstalled] scheduled task $taskName is not registered; run install.ps1" 1
}

$verb = if ($Start) { 'start' } else { 'stop' }
if ($Preflight) {
    Write-Output "preflight: would $verb scheduled task $taskName"
    Write-Output 'preflight: nothing changed'
    exit 0
}

if ($Start) {
    Start-ScheduledTask -TaskName $taskName
}
else {
    Stop-ScheduledTask -TaskName $taskName
}

Write-Output "$verb requested for scheduled task $taskName"
exit 0
