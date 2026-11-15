param(
    [ValidateNotNullOrEmpty()]
    [ValidateSet('ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent')]
    [string[]]$Modes = @('ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent'),
    [ValidateNotNullOrEmpty()]
    [ValidateSet('Balanced', 'Short', 'Over')]
    [string[]]$Scenarios = @('Balanced', 'Short', 'Over'),
    [switch]$ListCases
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
[System.Collections.Generic.HashSet[string]]$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$cases = foreach ($mode in $Modes) {
    foreach ($scenario in $Scenarios) {
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
