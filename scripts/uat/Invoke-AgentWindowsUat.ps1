#Requires -Version 7.4
<#
.SYNOPSIS
    Runs the Windows agent UAT driver (Preflight, StackOnly, Run or Cleanup).

.DESCRIPTION
    Agents may only run -Preflight and -StackOnly (D-S225). -Run changes this machine
    (services, HKLM, machine certificate stores) and must be run by a person from an
    elevated shell with -AcceptMachineChanges. See scripts/uat/README.md.

    Every run gets a folder under <repo>\.uat\agent-windows\<runId>. The driver writes
    results.json and results.md there. The folder path is printed first.

    Exit codes: 0 pass, 1 fail or inconclusive, 2 refused before any machine change,
    3 footprint left after cleanup, 4 internal error. The script exits with the driver's code.
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
$driverProject = Join-Path $repo 'tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj'
$driverExe = Join-Path $repo 'tests\Dhadgar.Agent.Windows.Uat\bin\Release\net10.0\Dhadgar.Agent.Windows.Uat.exe'
$nodesProject = Join-Path $repo 'src\Dhadgar.Nodes\Dhadgar.Nodes.csproj'
$gatewayProject = Join-Path $repo 'src\Dhadgar.Gateway\Dhadgar.Gateway.csproj'
$agentProject = Join-Path $repo 'src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj'

function New-RunId {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [System.Globalization.CultureInfo]::InvariantCulture)
    $suffix = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(2)).ToLowerInvariant()
    return "$stamp-$suffix"
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE"
    }
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
    if ($verb -eq 'run' -and $Mode -eq 'Msi') {
        [Console]::Error.WriteLine('-Mode Msi is built in P23 and is not available yet.')
        exit 2
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
    if ($verb -ne 'cleanup') {
        [System.IO.Directory]::CreateDirectory($runDir) | Out-Null
    }

    if (-not $NoBuild) {
        Invoke-DotNet -Arguments @('build', $driverProject, '-c', 'Release')
        if ($verb -eq 'stack-only' -or $verb -eq 'run') {
            Invoke-DotNet -Arguments @('build', $nodesProject, '-c', 'Release')
            Invoke-DotNet -Arguments @('build', $gatewayProject, '-c', 'Release')
        }
        if ($verb -eq 'run') {
            $agentOut = Join-Path $runDir 'agent'
            Invoke-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '-o', $agentOut)
        }
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
