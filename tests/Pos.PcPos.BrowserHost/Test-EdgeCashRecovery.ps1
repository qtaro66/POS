$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Join-Path $PSScriptRoot '../..'
Push-Location $repo
try {
    npm run build --workspace apps/pos-web -- --mode browser-fixture
    dotnet build tests/Pos.PcPos.BrowserHost
    $modes = 'ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent'
    $scenarios = 'Balanced', 'Short', 'Over'
    $passed = 0
    foreach ($mode in $modes) {
        foreach ($scenario in $scenarios) {
            Write-Host "Edge cash recovery: $mode / $scenario"
            node tests/Pos.PcPos.BrowserHost/browser-cash.cjs apps/pos-web/dist $scenario $mode
            $passed++
        }
    }
    Write-Host "Edge cash recovery: $passed/$($modes.Count * $scenarios.Count) passed"
} finally {
    Pop-Location
}
