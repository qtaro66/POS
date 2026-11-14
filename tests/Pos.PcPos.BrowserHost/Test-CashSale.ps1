param(
    [Parameter(Mandatory)][string]$Dist,
    [ValidateSet('Balanced', 'Short', 'Over')][string]$Scenario = 'Balanced'
)
$tender = if ($Scenario -eq 'Short') { 200 } else { 250 }
$countedCash = @{ Balanced = 700; Short = 690; Over = 710 }[$Scenario]
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Pos.PcPos.BrowserHost.csproj'
$distPath = (Resolve-Path -LiteralPath $Dist).Path
dotnet build $project -v quiet
if ($LASTEXITCODE) { throw 'Fixture build failed.' }
$assembly = Join-Path $PSScriptRoot 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'
$fixtureId = [guid]::NewGuid().ToString('N')
function Start-Fixture {
    Start-Job { dotnet $using:assembly $using:distPath $using:fixtureId }
}
$job = Start-Fixture
$centralJob = $null
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
    $pending = (Request "$api/floor-plan" 200 $null $secondHeaders).tables | Where-Object tableId -eq 'A01'
    if ($pending.status -ne 'PaymentPending') { throw 'Second device did not see the pending payment.' }
    $cashPath = "$api/tables/complete-cash-sale"
    $attempts = @(
        @{ Headers = $headers; Context = $context.Clone() }
        @{ Headers = $secondHeaders; Context = $context.Clone() }
    )
    $attempts[1].Context.deviceId = $devices[1].deviceId
    foreach ($attempt in $attempts) {
        $attempt.Context.requestId = [guid]::NewGuid().ToString()
        $attempt.Body = @{ schemaVersion = 1; context = $attempt.Context; expectedVersion = $version; tenderedAmount = $tender }
    }
    $responses = @(0, 1 | ForEach-Object -ThrottleLimit 2 -Parallel {
        $attempt = ($using:attempts)[$_]
        $response = Invoke-WebRequest -Uri "$using:origin/$using:cashPath" -Method Post -Headers $attempt.Headers `
            -ContentType application/json -Body (ConvertTo-Json -InputObject $attempt.Body -Depth 10 -Compress) `
            -NoProxy -SkipCertificateCheck -SkipHttpErrorCheck
        [pscustomobject]@{ Index = $_; Status = $response.StatusCode; Content = $response.Content }
    })
    if (($responses.Status | Sort-Object) -join ',' -ne '200,409') { throw 'Expected one successful settlement and one conflict.' }
    $winner = $responses | Where-Object Status -eq 200
    $sale = ConvertFrom-Json -InputObject $winner.Content
    $accepted = $attempts[$winner.Index]
    $replay = Request $cashPath 200 $accepted.Body $accepted.Headers
    if ($replay.receiptNumber -ne $sale.receiptNumber -or $replay.changeAmount -ne $sale.changeAmount) {
        throw 'Settlement replay differs.'
    }
    $available = (Request "$api/floor-plan" 200 $null $secondHeaders).tables | Where-Object tableId -eq 'A01'
    if ($available.status -ne 'Available' -or $available.tableSessionId) { throw 'Second device did not see the released table.' }
    if ($sale.appliedAmount -ne 200 -or $sale.changeAmount -ne ($tender - 200)) { throw 'Cash totals differ.' }
    $receipt = "$api/test-cash-cycle/receipts/$($sale.receiptNumber)"
    $null = Request $receipt 200 $null $headers
    $simulation = Request "$receipt/simulate" 200 @{ fail = $true } $headers
    $repeat = Request "$receipt/simulate" 200 @{ fail = $false } $secondHeaders
    if ($simulation.status -ne 'Failed' -or $repeat.status -ne $simulation.status) {
        throw 'Receipt simulation replay differs.'
    }
    function Read-CashOutbox {
        $status = Request "$api/cloud-outbox-status" 200 $null $secondHeaders
        $cash = @($status.items | Where-Object action -eq 'CompletePayment')
        if ($cash.Count -ne 1 -or $cash[0].requestId -ne $accepted.Context.requestId -or
            $cash[0].tableSessionId -ne $context.tableSessionId) {
            throw 'Expected exactly one unacknowledged cash event for the winning request.'
        }
        if ($status.summary.unacknowledgedCount -ne 4) {
            throw 'Expected four unacknowledged events for the cash cycle.'
        }
        $cash[0]
    }
    $beforeRestart = Read-CashOutbox
    Stop-Job $job
    Remove-Job $job
    $job = Start-Fixture
    $restart = @()
    for ($attempt = 0; $attempt -lt 60 -and -not ($restart -match '^Fixture second pairing:'); $attempt++) {
        Start-Sleep -Milliseconds 250
        $restart += Receive-Job $job
    }
    $pin = [regex]::Match(($restart -join "`n"), '(?m)^Fixture PIN: (\d+)$').Groups[1].Value
    if (-not $pin) { throw 'Restarted fixture did not start.' }
    foreach ($deviceHeaders in @($headers, $secondHeaders)) {
        $deviceHeaders.Remove('X-POS-Employee-Session')
        $freshLogin = Request 'api/v1/employee-sessions' 200 @{ schemaVersion = 1; pin = $pin } $deviceHeaders
        $deviceHeaders['X-POS-Employee-Session'] = $freshLogin.sessionToken
    }
    $recovered = Request $cashPath 200 $accepted.Body $accepted.Headers
    if ($recovered.receiptNumber -ne $sale.receiptNumber) { throw 'Cash settlement was lost after restart.' }
    $persisted = Request "$receipt/simulate" 200 @{ fail = $false } $secondHeaders
    if ($persisted.status -ne 'Failed') { throw 'Receipt outcome was lost after restart.' }
    $available = (Request "$api/floor-plan" 200 $null $secondHeaders).tables | Where-Object tableId -eq 'A01'
    if ($available.status -ne 'Available') { throw 'Table was not released after restart.' }
    $afterRestart = Read-CashOutbox
    if ($afterRestart.outboxId -ne $beforeRestart.outboxId -or
        $afterRestart.branchSequence -ne $beforeRestart.branchSequence) {
        throw 'Pending cash event identity changed after restart.'
    }
    $close = Request "$api/test-cash-cycle" 200 @{ operation = 'close'; shiftId = $shift; cash = $countedCash; pin = $pin } $headers
    if ($close.expectedCash -ne 700 -or $close.countedCash -ne $countedCash -or $close.difference -ne ($countedCash - 700)) { throw 'Shift totals differ.' }
    $centralJob = Start-Job { dotnet $using:assembly --central $using:fixtureId }
    $central = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        try {
            $central = Invoke-RestMethod 'https://127.0.0.1:17444/fixture-events' -NoProxy -SkipCertificateCheck -TimeoutSec 2
        }
        catch {
            if ($centralJob.State -in 'Failed', 'Completed', 'Stopped') { throw 'Central fixture stopped before reconnect.' }
            continue
        }
        $queue = Request "$api/cloud-outbox-status" 200 $null $headers
        if ($queue.summary.unacknowledgedCount -eq 0 -and $central.events.Count -eq 4 -and $central.duplicates -eq 1) { break }
    }
    $receivedCash = @($central.events | Where-Object action -eq 'CompletePayment')
    if ($queue.summary.unacknowledgedCount -ne 0 -or ($central.events.branchSequence -join ',') -ne '1,2,3,4' -or
        $receivedCash.Count -ne 1 -or $receivedCash[0].eventId -ne $beforeRestart.outboxId -or $central.duplicates -ne 1) {
        Receive-Job $job | Select-String -Pattern "fail:|Exception|status code|https://127.0.0.1:17444" | Select-Object -Last 20 | Write-Host
        throw "Reconnect failed. Central count: $($central.events.Count), duplicates: $($central.duplicates), outstanding: $($queue.summary.unacknowledgedCount), statuses: $($queue.items.processingStatus -join ',')."
    }
    "PC POS loopback HTTPS cash sale ($Scenario): PASS"
}
finally {
    if ($centralJob) { Stop-Job $centralJob; Remove-Job $centralJob }
    Stop-Job $job
    Remove-Job $job
    $fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) "pc-pos-browser-$fixtureId"
    if (Test-Path -LiteralPath $fixtureDirectory) {
        $resolvedFixture = (Resolve-Path -LiteralPath $fixtureDirectory).Path
        if ($resolvedFixture -ne [IO.Path]::GetFullPath($fixtureDirectory)) { throw 'Unexpected fixture path.' }
        Remove-Item -LiteralPath $resolvedFixture -Recurse
    }
}
