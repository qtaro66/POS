$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Join-Path $PSScriptRoot '../..'
Push-Location $repo
try {
    npm run build --workspace apps/pos-web -- --mode browser-fixture
    dotnet build tests/Pos.PcPos.BrowserHost
    foreach ($mode in 'ReloadLost', 'ReloadUnsent') {
        foreach ($scenario in 'Balanced', 'Short', 'Over') {
            Write-Host "Edge cash recovery: $mode / $scenario"
            node tests/Pos.PcPos.BrowserHost/browser-cash.cjs apps/pos-web/dist $scenario $mode
        }
    }
    Write-Host 'Edge cash recovery: 6/6 passed'
} finally {
    Pop-Location
}
