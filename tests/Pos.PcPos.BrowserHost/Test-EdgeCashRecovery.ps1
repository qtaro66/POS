param(
    [ValidateNotNullOrEmpty()]
    [ValidateSet('Concurrent', 'LostResponse', 'ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent', 'MultiSale', 'ShiftHandoff', 'ShiftTransitions', 'BranchIsolation')]
    [string[]]$Modes = @('Concurrent', 'LostResponse', 'ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent', 'MultiSale', 'ShiftHandoff', 'ShiftTransitions', 'BranchIsolation'),
    [ValidateNotNullOrEmpty()]
    [ValidateSet('Balanced', 'Short', 'Over')]
    [string[]]$Scenarios = @('Balanced', 'Short', 'Over'),
    [ValidateNotNullOrEmpty()]
    [ValidateSet('Balanced', 'Short', 'Over')]
    [string[]]$NextShiftScenarios = @('Balanced', 'Short', 'Over'),
    [switch]$ListCases,
    [switch]$ContinueOnFailure,
    [ValidateNotNullOrEmpty()]
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
[System.Collections.Generic.HashSet[string]]$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$cases = foreach ($requestedMode in $Modes) {
    $mode = $MyInvocation.MyCommand.Parameters['Modes'].Attributes.ValidValues -eq $requestedMode | Select-Object -First 1
    if ($mode -in 'ShiftTransitions', 'BranchIsolation') {
        if ('Balanced' -in $Scenarios -and $seen.Add("$mode/Balanced")) {
            [pscustomobject]@{ Mode = $mode; Scenario = 'Balanced'; NextShiftScenario = $null }
        }
        continue
    }
    foreach ($requestedScenario in $Scenarios) {
        $scenario = $MyInvocation.MyCommand.Parameters['Scenarios'].Attributes.ValidValues -eq $requestedScenario | Select-Object -First 1
        if ($mode -eq 'ShiftHandoff') {
            foreach ($requestedNext in $NextShiftScenarios) {
                $next = $MyInvocation.MyCommand.Parameters['NextShiftScenarios'].Attributes.ValidValues -eq $requestedNext | Select-Object -First 1
                if ($seen.Add("$mode/$scenario/$next")) {
                    [pscustomobject]@{ Mode = $mode; Scenario = $scenario; NextShiftScenario = $next }
                }
            }
            continue
        }
        if ($seen.Add("$mode/$scenario")) {
            [pscustomobject]@{ Mode = $mode; Scenario = $scenario; NextShiftScenario = $null }
        }
    }
}
if (-not $cases) {
    throw 'No Edge cash recovery cases match the selected modes and scenarios.'
}
if ($ListCases) {
    $cases
    return
}

$reportFile = if ($ReportPath) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath) }
if ($reportFile -and (Test-Path -LiteralPath $reportFile)) {
    throw "Edge report already exists: $reportFile"
}
function Write-EdgeReport([string]$State, [switch]$Initial) {
    $report = [pscustomobject]@{
        State = $State
        Selected = $cases.Count
        Attempted = $results.Count
        Passed = $passed
        Failed = $results.Count - $passed
        Cases = $results.ToArray()
    }
    $temp = "$reportFile.$([guid]::NewGuid()).tmp"
    try {
        [IO.File]::WriteAllText($temp, ($report | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        [IO.File]::Move($temp, $reportFile, -not $Initial)
    } finally {
        [IO.File]::Delete($temp)
    }
}

$repo = Join-Path $PSScriptRoot '../..'
Push-Location $repo
try {
    npm run build --workspace apps/pos-web -- --mode browser-fixture
    dotnet build tests/Pos.PcPos.BrowserHost
    $results = [System.Collections.Generic.List[object]]::new()
    $passed = 0
    if ($reportFile) { Write-EdgeReport 'Running' -Initial }
    foreach ($case in $cases) {
        $arguments = @('tests/Pos.PcPos.BrowserHost/browser-cash.cjs', 'apps/pos-web/dist', $case.Scenario, $case.Mode)
        $label = "$($case.Mode) / $($case.Scenario)"
        if ($case.NextShiftScenario) {
            $arguments += $case.NextShiftScenario
            $label += " / $($case.NextShiftScenario)"
        }
        Write-Host "Edge cash recovery: $label"
        $elapsed = [Diagnostics.Stopwatch]::StartNew()
        try {
            $PSNativeCommandUseErrorActionPreference = $false
            node @arguments
            $exitCode = $LASTEXITCODE
        } finally {
            $PSNativeCommandUseErrorActionPreference = $true
        }
        if ($exitCode -eq 0) { $passed++ }
        $results.Add([pscustomobject]@{
            Mode = $case.Mode
            Scenario = $case.Scenario
            NextShiftScenario = $case.NextShiftScenario
            ExitCode = $exitCode
            Seconds = [Math]::Round($elapsed.Elapsed.TotalSeconds, 2)
        })
        $stop = $exitCode -ne 0 -and -not $ContinueOnFailure
        if ($reportFile) {
            $reportState = if ($results.Count -eq $cases.Count) { 'Completed' } elseif ($stop) { 'StoppedOnFailure' } else { 'Running' }
            Write-EdgeReport $reportState
        }
        if ($stop) { break }
    }
    Write-Host "Edge cash recovery: $passed/$($cases.Count) passed"
    if ($passed -ne $cases.Count) { exit 1 }
} finally {
    Pop-Location
}
