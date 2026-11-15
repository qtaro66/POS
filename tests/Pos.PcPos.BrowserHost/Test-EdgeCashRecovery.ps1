param(
    [ValidateNotNullOrEmpty()]
    [ValidateSet('Concurrent', 'LostResponse', 'ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent', 'MultiSale')]
    [string[]]$Modes = @('Concurrent', 'LostResponse', 'ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent', 'MultiSale'),
    [ValidateNotNullOrEmpty()]
    [ValidateSet('Balanced', 'Short', 'Over')]
    [string[]]$Scenarios = @('Balanced', 'Short', 'Over'),
    [switch]$ListCases
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
[System.Collections.Generic.HashSet[string]]$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$cases = foreach ($requestedMode in $Modes) {
    $mode = $MyInvocation.MyCommand.Parameters['Modes'].Attributes.ValidValues -eq $requestedMode | Select-Object -First 1
    foreach ($requestedScenario in $Scenarios) {
        $scenario = $MyInvocation.MyCommand.Parameters['Scenarios'].Attributes.ValidValues -eq $requestedScenario | Select-Object -First 1
        if ($seen.Add("$mode/$scenario")) {
            [pscustomobject]@{ Mode = $mode; Scenario = $scenario }
        }
    }
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
        Write-Host "Edge cash recovery: $($case.Mode) / $($case.Scenario)"
        node tests/Pos.PcPos.BrowserHost/browser-cash.cjs apps/pos-web/dist $case.Scenario $case.Mode
        $passed++
    }
    Write-Host "Edge cash recovery: $passed/$($cases.Count) passed"
} finally {
    Pop-Location
}
