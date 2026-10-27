#Requires -Version 7.4
<#
.SYNOPSIS
    Runs the Windows agent UAT driver (Preflight, StackOnly, Run or Cleanup). Run modes are Process, Service and Msi.

.DESCRIPTION
    Anyone may run -Preflight and -StackOnly. -Mode changes this machine (services, HKLM,
    machine certificate stores) and needs an elevated shell and -AcceptMachineChanges. Under
    CLAUDECODE (an agent run, D-S239) it also needs a clean git tree and no -NoBuild, and an
    agent's -CleanupOnly works only on a run folder whose launch.json says agent. See
    scripts/uat/README.md.

    Every run gets a folder under <repo>\.uat\agent-windows\<runId>. The driver writes
    results.json and results.md there. The folder path is printed first.

    Exit codes: 0 pass, 1 fail or inconclusive, 2 refused before any machine change,
    3 footprint left after cleanup, 4 internal error. The script exits with the driver's code.

    It builds through Build-AgentWindowsUat.ps1 (locked, RID-less restore, then --no-restore; P32), so a build never changes a tracked file.
#>
[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(ParameterSetName = 'Run', Mandatory)][ValidateSet('Process', 'Service', 'Msi')][string] $Mode,   # 'Service' from P22, 'Msi' from P23
    [Parameter(ParameterSetName = 'Run', Mandatory)][switch] $AcceptMachineChanges,
    [Parameter(ParameterSetName = 'Preflight')][switch] $Preflight,
    [Parameter(ParameterSetName = 'StackOnly', Mandatory)][switch] $StackOnly,
    [Parameter(ParameterSetName = 'Cleanup', Mandatory)][string] $CleanupOnly,   # a run folder
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$uatRoot = Join-Path $repo '.uat\agent-windows'
$driverExe = Join-Path $repo 'tests\Dhadgar.Agent.Windows.Uat\bin\Release\net10.0\Dhadgar.Agent.Windows.Uat.exe'
$installerProject = Join-Path $repo 'src\Agents\Dhadgar.Agent.Windows\Installer\Dhadgar.Agent.Windows.Installer.wixproj'

function New-RunId {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [System.Globalization.CultureInfo]::InvariantCulture)
    $suffix = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(2)).ToLowerInvariant()
    return "$stamp-$suffix"
}

try {
    $verb = switch ($PSCmdlet.ParameterSetName) {
        'Run' { 'run' }
        'StackOnly' { 'stack-only' }
        'Cleanup' { 'cleanup' }
        default { 'preflight' }
    }

    if ($verb -eq 'run' -and -not $AcceptMachineChanges) {
        [Console]::Error.WriteLine('run changes this machine: pass -AcceptMachineChanges')
        exit 2
    }
    # Cheap guards run before the build and the run folder, so a refused call changes nothing.
    # The driver repeats them and stays authoritative.
    if ($verb -eq 'run' -and (Test-Path env:CLAUDECODE)) {
        if ($NoBuild) {
            [Console]::Error.WriteLine('run refused: an agent run always builds (D-S239); drop -NoBuild')
            exit 2
        }
        $treeArgs = @('-C', $repo, 'status', '--porcelain=v1', '--untracked-files=all', '--', '.', ':(exclude)docs', ':(exclude).claude')
        try {
            $tree = & git @treeArgs
            $treeExit = $LASTEXITCODE
        }
        catch {
            $tree = $null
            $treeExit = 1
        }
        if ($treeExit -ne 0) {
            [Console]::Error.WriteLine('run refused: tree check failed (D-S239)')
            exit 2
        }
        if ($tree) {
            [Console]::Error.WriteLine('run refused: agent run needs a clean tree (D-S239)')
            exit 2
        }
    }
    if ($verb -eq 'run') {
        $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            [Console]::Error.WriteLine('run refused: not elevated')
            exit 2
        }
    }

    if ($verb -eq 'cleanup') {
        $runDir = [System.IO.Path]::GetFullPath($CleanupOnly)
        $runId = Split-Path -Leaf $runDir
    }
    else {
        $runId = New-RunId
        $runDir = Join-Path $uatRoot $runId
    }

    Write-Output "Run folder: $runDir"
    # stack-only must find no run folder (the driver refuses one that exists, N1), so the driver creates it.
    if ($verb -ne 'cleanup' -and $verb -ne 'stack-only') {
        [System.IO.Directory]::CreateDirectory($runDir) | Out-Null
    }

    if (-not $NoBuild) {
        # One build path for every verb (P32, D-S241): locked, RID-less restores, then --no-restore builds,
        # so the build never rewrites a tracked lock file and an agent run keeps its clean tree (D-S239).
        $buildArgs = @{ Verb = $verb }
        if ($verb -eq 'run') {
            $buildArgs.Mode = $Mode
            if ($Mode -ne 'Msi') {
                $buildArgs.AgentOut = Join-Path $runDir 'agent'
            }
        }
        & (Join-Path $PSScriptRoot 'Build-AgentWindowsUat.ps1') @buildArgs
    }

    if ($verb -eq 'run' -and $Mode -eq 'Msi') {
        # Copy the freshly built MSI into the run folder; the driver installs only from <runDir>\msi.
        # Exactly one candidate is accepted: more than one means stale output, which must not be installed (P23 review).
        $installerRelease = Join-Path (Split-Path -Parent $installerProject) 'bin\Release'
        $msiCandidates = @(Get-ChildItem -LiteralPath $installerRelease -Filter 'MeridianConsoleAgent.msi' -Recurse -File -ErrorAction SilentlyContinue)
        if ($msiCandidates.Count -ne 1) {
            [Console]::Error.WriteLine("Expected one MeridianConsoleAgent.msi under $installerRelease, found $($msiCandidates.Count). Run without -NoBuild.")
            exit 4
        }
        $builtMsi = $msiCandidates[0]
        $msiDir = Join-Path $runDir 'msi'
        [System.IO.Directory]::CreateDirectory($msiDir) | Out-Null
        Copy-Item -LiteralPath $builtMsi.FullName -Destination (Join-Path $msiDir 'MeridianConsoleAgent.msi')
    }

    if (-not (Test-Path -LiteralPath $driverExe -PathType Leaf)) {
        [Console]::Error.WriteLine("Driver not built: $driverExe. Run without -NoBuild.")
        exit 4
    }

    $driverArgs = @($verb, '--repo-root', $repo, '--run-id', $runId)
    if ($verb -eq 'run') {
        $driverArgs += @('--mode', $Mode.ToLowerInvariant(), '--accept-machine-changes')
    }
    if ($verb -eq 'cleanup') {
        $driverArgs += @('--run-dir', $runDir)
    }

    & $driverExe @driverArgs
    exit $LASTEXITCODE
}
catch {
    [Console]::Error.WriteLine("Invoke-AgentWindowsUat failed: $($_.Exception.Message)")
    exit 4
}
