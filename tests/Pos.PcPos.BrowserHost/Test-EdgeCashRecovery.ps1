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
    [switch]$ListCases
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

$repo = Join-Path $PSScriptRoot '../..'
Push-Location $repo
try {
    npm run build --workspace apps/pos-web -- --mode browser-fixture
    dotnet build tests/Pos.PcPos.BrowserHost
    $passed = 0
    foreach ($case in $cases) {
        $arguments = @('tests/Pos.PcPos.BrowserHost/browser-cash.cjs', 'apps/pos-web/dist', $case.Scenario, $case.Mode)
        $label = "$($case.Mode) / $($case.Scenario)"
        if ($case.NextShiftScenario) {
            $arguments += $case.NextShiftScenario
            $label += " / $($case.NextShiftScenario)"
        }
        Write-Host "Edge cash recovery: $label"
        node @arguments
        $passed++
    }
    Write-Host "Edge cash recovery: $passed/$($cases.Count) passed"
} finally {
    Pop-Location
}
