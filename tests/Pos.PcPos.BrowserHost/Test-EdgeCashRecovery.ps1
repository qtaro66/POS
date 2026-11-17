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
    [string]$ReportPath,
    [ValidateNotNullOrEmpty()]
    [string]$RerunFromReport
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
if ($RerunFromReport) {
    $previous = Get-Content -LiteralPath $RerunFromReport -Raw | ConvertFrom-Json
    if (-not $previous.PlannedCases -or $previous.Attempted -ne @($previous.Cases).Count -or
        $previous.Selected -ne @($previous.PlannedCases).Count -or $previous.Attempted -gt $previous.Selected) {
        throw 'Invalid Edge report case plan or results.'
    }
    $requested = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $previous.PlannedCases.Count; $index++) {
        $planned = $previous.PlannedCases[$index]
        $key = "$($planned.Mode)/$($planned.Scenario)/$($planned.NextShiftScenario)"
        if ($index -lt $previous.Attempted) {
            $completed = $previous.Cases[$index]
            if ("$($completed.Mode)/$($completed.Scenario)/$($completed.NextShiftScenario)" -ne $key -or
                $null -eq $completed.ExitCode) {
                throw 'Invalid Edge report result order or exit code.'
            }
            if ($completed.ExitCode -eq 0) { continue }
        }
        [void]$requested.Add($key)
    }
    $cases = @($cases | Where-Object { $requested.Contains("$($_.Mode)/$($_.Scenario)/$($_.NextShiftScenario)") })
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
        Commit = $commit
        WorkingTreeClean = $workingTreeClean
        StartedUtc = $startedUtc
        UpdatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'")
        ContinueOnFailure = [bool]$ContinueOnFailure
        PlannedCases = @($cases)
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
    if ($reportFile) {
        $commit = git rev-parse HEAD
        $workingTreeClean = -not (git status --porcelain --untracked-files=all)
        $startedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'")
    }
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
