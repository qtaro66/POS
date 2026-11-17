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
$repo = Join-Path $PSScriptRoot '../..'
Push-Location $repo
try {
    npm run build --workspace apps/pos-web -- --mode browser-fixture
    dotnet build tests/Pos.PcPos.BrowserHost
    $results = [System.Collections.Generic.List[object]]::new()
    $passed = 0
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
        if ($exitCode -ne 0 -and -not $ContinueOnFailure) { break }
    }
    if ($reportFile) {
        $report = [pscustomobject]@{
            Selected = $cases.Count
            Attempted = $results.Count
            Passed = $passed
            Failed = $results.Count - $passed
            Cases = $results.ToArray()
        }
        $stream = [IO.File]::Open($reportFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try {
            $json = [Text.Encoding]::UTF8.GetBytes(($report | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
            $stream.Write($json)
        } finally {
            $stream.Dispose()
        }
    }
    Write-Host "Edge cash recovery: $passed/$($cases.Count) passed"
    if ($passed -ne $cases.Count) { exit 1 }
} finally {
    Pop-Location
}
