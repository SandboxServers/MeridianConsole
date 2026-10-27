#Requires -Version 7.4
<#
.SYNOPSIS
    Evaluates every LaunchCondition row of a built MSI on this machine, without installing anything (P34, D-S242).

.DESCRIPTION
    The script copies the MSI to a temp file and reads its LaunchCondition table read-only. It opens a session with
    msiOpenPackageFlagsIgnoreMachineState, runs AppSearch (registry reads only) and evaluates each row. It never
    calls msiexec, writes nothing outside the temp copy and deletes that copy on exit.

    Exit codes: 0 = pass, 1 = a check failed, 2 = could not evaluate (report it as blocked).

    Checks:
      1. No row references the WindowsBuild property. Under msiexec on Windows 10/11, the server reports
         WindowsBuild = 9600 and VersionNT = 603 (the Windows 8.1 values), so a WindowsBuild row always fails there.
      2. The row 'Installed OR WINDOWS_BUILD_NUMBER >= 10240' exists.
      3. AppSearch sets WINDOWS_BUILD_NUMBER to the registry's CurrentBuildNumber.
      4. Every row except 'Privileged' is TRUE, with CONTROL_PLANE_ENDPOINT set as the UAT passes it.
         Privileged depends on this shell's elevation; the UAT Msi run proves it.
      5. The build row is FALSE for 9600, 10239 and an empty value, and TRUE for 10240 and 26300.

    Limit: in process, Windows Installer reports the real WindowsBuild (for example 26300), so this script cannot
    reproduce the server-side 9600. That is why check 1 rejects the property by name. The server-side proof is the
    UAT Msi run, whose MSI log must show "Property(S): WINDOWS_BUILD_NUMBER = <build>".
#>
param(
    [Parameter(Mandatory)][string] $MsiPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expected = 'Installed OR WINDOWS_BUILD_NUMBER >= 10240'
$names = 'FALSE', 'TRUE', 'NONE', 'ERROR'

function Invoke-Com($obj, [string] $name, [System.Reflection.BindingFlags] $kind, [object[]] $a = @()) {
    return $obj.GetType().InvokeMember($name, $kind, $null, $obj, $a)
}
$M = [System.Reflection.BindingFlags]::InvokeMethod
$G = [System.Reflection.BindingFlags]::GetProperty
$S = [System.Reflection.BindingFlags]::SetProperty

if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
    "blocked: no MSI at $MsiPath"
    exit 2
}

# A plain [string]: Join-Path returns a PSObject-wrapped string, which COM rejects (DISP_E_TYPEMISMATCH).
[string] $copy = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'p34-' + [guid]::NewGuid().ToString('N') + '.msi')
Copy-Item -LiteralPath $MsiPath -Destination $copy
$failures = [System.Collections.Generic.List[string]]::new()
$code = 0
$installer = $null
$session = $null
try {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    Invoke-Com $installer 'UILevel' $S @(2) | Out-Null   # msiUILevelNone

    # Read the LaunchCondition rows (database opened read-only), then release the database before OpenPackage.
    $db = Invoke-Com $installer 'OpenDatabase' $M @($copy, 0)
    $view = Invoke-Com $db 'OpenView' $M @('SELECT `Condition` FROM `LaunchCondition`')
    Invoke-Com $view 'Execute' $M @($null) | Out-Null
    $rows = [System.Collections.Generic.List[string]]::new()
    while ($true) {
        $rec = Invoke-Com $view 'Fetch' $M
        if ($null -eq $rec) { break }
        $rows.Add([string](Invoke-Com $rec 'StringData' $G @(1)))
    }
    Invoke-Com $view 'Close' $M | Out-Null
    [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null
    $db = $null; $view = $null; $rec = $null
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()

    # Check 1 and 2: the row text.
    foreach ($c in $rows) {
        if ($c -cmatch '\bWindowsBuild\b') {
            $failures.Add("row uses the WindowsBuild property, which msiexec reports as 9600 on Windows 10/11 (D-S242): $c")
        }
    }
    if (-not ($rows -ccontains $expected)) {
        $failures.Add("no row '$expected'")
    }

    # Check 3: AppSearch reads the real build number.
    $registry = [string](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuildNumber).CurrentBuildNumber
    $session = Invoke-Com $installer 'OpenPackage' $M @($copy, 1)   # msiOpenPackageFlagsIgnoreMachineState
    'in-process WindowsBuild = {0}, VersionNT = {1}, VersionNT64 = {2}, Privileged = {3}' -f `
        (Invoke-Com $session 'Property' $G @('WindowsBuild')), (Invoke-Com $session 'Property' $G @('VersionNT')),
        (Invoke-Com $session 'Property' $G @('VersionNT64')), (Invoke-Com $session 'Property' $G @('Privileged'))
    $status = Invoke-Com $session 'DoAction' $M @('AppSearch')
    "AppSearch -> $status (1 = success)"
    $found = [string](Invoke-Com $session 'Property' $G @('WINDOWS_BUILD_NUMBER'))
    "WINDOWS_BUILD_NUMBER = '$found'; registry CurrentBuildNumber = '$registry'"
    if ($found -cne $registry) {
        $failures.Add("AppSearch set WINDOWS_BUILD_NUMBER to '$found', not the registry's '$registry'")
    }

    # Check 4: every row, with the endpoint the UAT passes.
    Invoke-Com $session 'Property' $S @('CONTROL_PLANE_ENDPOINT', 'https://localhost:55204/') | Out-Null
    foreach ($c in $rows) {
        $e = [int](Invoke-Com $session 'EvaluateCondition' $M @($c))   # 0 false, 1 true, 2 none, 3 error
        '[{0}] {1}' -f $names[$e], $c
        if ($c -ceq 'Privileged') { continue }
        if ($e -ne 1) { $failures.Add("row is $($names[$e]), not TRUE: $c") }
    }

    # Check 5: forced build values against the expected row.
    $cases = @(@('9600', $false), @('10239', $false), @('10240', $true), @('26300', $true), @('', $false))
    foreach ($case in $cases) {
        Invoke-Com $session 'Property' $S @('WINDOWS_BUILD_NUMBER', $case[0]) | Out-Null
        $e = [int](Invoke-Com $session 'EvaluateCondition' $M @($expected))
        "forced WINDOWS_BUILD_NUMBER = '{0}' -> {1}" -f $case[0], $names[$e]
        if (($e -eq 1) -ne $case[1]) {
            $failures.Add("forced '$($case[0])' gave $($names[$e]); expected $(if ($case[1]) { 'TRUE' } else { 'FALSE' })")
        }
    }

    foreach ($f in $failures) { "FAIL: $f" }
    "failures = $($failures.Count)"
    $code = if ($failures.Count -eq 0) { 0 } else { 1 }
}
catch {
    "blocked: could not evaluate: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    $code = 2
}
finally {
    if ($null -ne $session) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) | Out-Null }
    if ($null -ne $installer) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null }
    $session = $null; $installer = $null
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
}
exit $code
