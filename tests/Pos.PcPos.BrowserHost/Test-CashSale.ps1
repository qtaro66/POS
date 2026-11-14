param([Parameter(Mandatory)][string]$Dist)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Pos.PcPos.BrowserHost.csproj'
$distPath = (Resolve-Path -LiteralPath $Dist).Path
dotnet build $project -v quiet
if ($LASTEXITCODE) { throw 'Fixture build failed.' }
$assembly = Join-Path $PSScriptRoot 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'
$job = Start-Job { dotnet $using:assembly $using:distPath }
try {
    $lines = @()
    for ($attempt = 0; $attempt -lt 60 -and -not ($lines -match '^Fixture second pairing:'); $attempt++) {
        Start-Sleep -Milliseconds 250
        $lines += Receive-Job $job
    }
    $origin = 'https://127.0.0.1:17443'
    $pin = [regex]::Match(($lines -join "`n"), '(?m)^Fixture PIN: (\d+)$').Groups[1].Value
    $links = @($lines | Where-Object { $_ -match '^Fixture (second )?pairing:' })
    if (-not $pin -or $links.Count -ne 2) { throw 'Fixture did not start.' }
    function Request($Path, $Expected, $Body = $null, $Headers = @{}) {
        $options = @{ Uri = "$origin/$Path"; Headers = $Headers; SkipCertificateCheck = $true; NoProxy = $true; SkipHttpErrorCheck = $true }
        if ($null -ne $Body) {
            $options.Method = 'Post'
            $options.ContentType = 'application/json'
            $options.Body = ConvertTo-Json -InputObject $Body -Compress -Depth 10
        }
        $response = Invoke-WebRequest @options
        if ($response.StatusCode -ne $Expected) { throw "$Path returned $($response.StatusCode); expected $Expected." }
        if ($response.Content -and $response.Headers['Content-Type'] -match 'json') { ConvertFrom-Json -InputObject $response.Content }
    }
    $null = Request '' 200
    $devices = foreach ($link in $links) {
        $fields = [regex]::Match($link, 'branchId=([^&]+)&pairingId=([^&]+)&token=(.+)$').Groups
        Request 'api/v1/pairing/redemptions' 200 @{
            schemaVersion = 1; branchId = $fields[1].Value; pairingId = $fields[2].Value
            oneTimeToken = $fields[3].Value; deviceDisplayName = 'PC POS'
        }
    }
    $headers = @{ Authorization = 'Device ' + $devices[0].deviceCredential }
    $null = Request 'api/v1/employee-sessions' 401 @{ schemaVersion = 1; pin = '000000' } $headers
    $login = Request 'api/v1/employee-sessions' 200 @{ schemaVersion = 1; pin = $pin } $headers
    $headers['X-POS-Employee-Session'] = $login.sessionToken
    $secondHeaders = @{ Authorization = 'Device ' + $devices[1].deviceCredential }
    $secondLogin = Request 'api/v1/employee-sessions' 200 @{ schemaVersion = 1; pin = $pin } $secondHeaders
    $secondHeaders['X-POS-Employee-Session'] = $secondLogin.sessionToken
    $api = 'api/v1/ordering'
    $plan = Request "$api/floor-plan" 200 $null $secondHeaders
    if ($plan.tables.Count -ne 2) { throw 'Expected two fixture tables.' }
    $shift = [guid]::NewGuid().ToString()
    $null = Request "$api/test-cash-cycle" 200 @{ operation = 'open'; shiftId = $shift; cash = 500; pin = $pin } $headers
    $context = @{
        branchId = 'BROWSER-TEST'; deviceId = $devices[0].deviceId; employeeId = $login.employeeId
        tableSessionId = [guid]::NewGuid().ToString(); clientCreatedAt = [DateTime]::UtcNow.ToString('O')
    }
    function Command($Path, $Expected, $Fields) {
        $context.requestId = [guid]::NewGuid().ToString()
        Request "$api/$Path" $Expected (@{ schemaVersion = 1; context = $context } + $Fields) $headers
    }
    $null = Command 'tables/open' 200 @{ tableId = 'A01' }
    $null = Command 'orders' 200 @{ lines = @(@{ sellableItemId = 'ITEM-1'; quantity = 2; fulfillmentType = 'DineIn'; options = @() }) }
    $detail = Request "$api/table-sessions/$($context.tableSessionId)" 200 $null $headers
    $payment = Command 'tables/request-payment' 200 @{ expectedVersion = $detail.session.tableVersion }
    $version = $payment.result.tableVersion
    $null = Command 'tables/complete-cash-sale' 409 @{ expectedVersion = $version; tenderedAmount = 199 }
    $sale = Command 'tables/complete-cash-sale' 200 @{ expectedVersion = $version; tenderedAmount = 250 }
    if ($sale.appliedAmount -ne 200 -or $sale.changeAmount -ne 50) { throw 'Cash totals differ.' }
    $receipt = "$api/test-cash-cycle/receipts/$($sale.receiptNumber)"
    $null = Request $receipt 200 $null $headers
    $null = Request "$receipt/simulate" 200 @{ fail = $false } $headers
    $close = Request "$api/test-cash-cycle" 200 @{ operation = 'close'; shiftId = $shift; cash = 700; pin = $pin } $headers
    if ($close.expectedCash -ne 700 -or $close.difference -ne 0) { throw 'Shift totals differ.' }
    'PC POS loopback HTTPS cash sale: PASS'
}
finally {
    Stop-Job $job
    Remove-Job $job
}
