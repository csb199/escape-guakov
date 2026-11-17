param([ValidateSet('HybridCLR','Mono')][string]$Backend = 'HybridCLR', [string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
$project = if ($ProjectRoot) { [IO.Path]::GetFullPath($ProjectRoot) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
$player = Join-Path $project "Builds/$Backend/EnergyCollector.exe"
if (!(Test-Path $player)) { throw "Build $Backend first." }
$results = Join-Path $project ('TestResults/' + $Backend + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $results | Out-Null
$playerStarts = 0
foreach ($version in @(1,2)) {
    if (Test-Path "$results/Data/shutdown-check.txt") { Remove-Item -LiteralPath "$results/Data/shutdown-check.txt" }
    $extra = if ($version -eq 1) { '--stage-update' } else { '' }
    $arguments = "-batchmode --self-test $extra `"--data=$results/Data`" -logFile `"$results/player-v$version.log`""
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $playerStarts++
    if (!$process.WaitForExit(90000)) { $process.Kill(); throw 'Demo self-test exceeded 90 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Demo self-test failed. See $results" }
    $report = Get-Content "$results/Data/demo-report.json" -Raw | ConvertFrom-Json
    $expected = if ($version -eq 1) { '1.0.0' } else { '1.1.0' }
    if (!$report.passed -or $report.version -ne $expected -or !$report.codeFlavor.StartsWith("V$version /")) { throw 'Version or self-test result mismatch.' }
    if ($report.checks.Count -lt 62 -or $report.checks -notcontains 'wrong asset type does not leak a resource lease') { throw 'Missing resource lease regression check.' }
    if ($report.checks -notcontains 'resource shutdown drains active and queued cache trim operations' -or $report.checks -notcontains 'master mute applies immediately during audio fade-out') { throw 'Missing expansion regression checks.' }
    if (!(Test-Path "$results/Data/shutdown-check.txt")) { throw 'Missing shutdown verification.' }
    Copy-Item "$results/Data/demo-report.json" "$results/v$version-report.json"
    if (Test-Path "$results/Data/demo-arena.png") { Copy-Item "$results/Data/demo-arena.png" "$results/v$version-arena.png" }
}
$recoveryData = Join-Path $results 'Recovery'
foreach ($step in @(1,2,3)) {
    $extra = switch ($step) { 1 { '--stage-update' } 2 { '--fail-before-ready' } 3 { '' } }
    $arguments = "-batchmode --self-test $extra `"--data=$recoveryData`" -logFile `"$results/recovery-$step.log`""
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $playerStarts++
    if (!$process.WaitForExit(90000)) { $process.Kill(); throw 'Recovery test exceeded 90 seconds.' }
    if ($step -eq 2) {
        if ($process.ExitCode -eq 0) { throw 'Expected a failed candidate boot.' }
        $failure = Get-Content "$recoveryData/failure.txt" -Raw
        if (!$failure.Contains('Requested test fault')) { throw "Unexpected failure: $failure" }
    } else {
        if ($process.ExitCode -ne 0) { throw "Recovery step $step failed." }
        $report = Get-Content "$recoveryData/demo-report.json" -Raw | ConvertFrom-Json
        if (!$report.passed -or $report.version -ne '1.0.0' -or !$report.codeFlavor.StartsWith('V1 /')) { throw 'Recovery did not restore V1.' }
    }
}
Copy-Item "$recoveryData/demo-report.json" "$results/recovery-report.json"
# These cases deliberately omit --self-test: exercise ordinary startup failure and wantsToQuit.
foreach ($scenario in @('NormalFailure','NormalQuit')) {
    $scenarioData = Join-Path $results $scenario
    $extra = if ($scenario -eq 'NormalFailure') { '--fail-before-ready --exit-on-startup-failure' } else { '--quit-after-ready' }
    $arguments = "-batchmode --validate-shutdown $extra `"--data=$scenarioData`" -logFile `"$results/$scenario.log`""
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $playerStarts++
    if (!$process.WaitForExit(90000)) { $process.Kill(); throw "Timed out: $scenario" }
    $expected = if ($scenario -eq 'NormalFailure') { 1 } else { 0 }
    if ($process.ExitCode -ne $expected) { throw "Unexpected exit code in $scenario" }
    if (!(Test-Path "$scenarioData/async-stop-completed.txt") -or !(Test-Path "$scenarioData/shutdown-check.txt")) {
        throw "Ordinary $scenario skipped asynchronous cleanup."
    }
    if ($scenario -eq 'NormalFailure' -and !(Get-Content "$scenarioData/failure.txt" -Raw).Contains('Requested test fault')) {
        throw 'Wrong failure reached in ordinary startup test.'
    }
}
function Invoke-RecoveryLaunch([string]$Name, [string]$Data, [string]$Extra, [int]$ExpectedCode, [string]$ExpectedVersion) {
    $shutdownMarker = Join-Path $Data 'shutdown-check.txt'
    if (Test-Path -LiteralPath $shutdownMarker) { Remove-Item -LiteralPath $shutdownMarker }
    $demoReport = Join-Path $Data 'demo-report.json'
    if ($ExpectedVersion -and (Test-Path -LiteralPath $demoReport)) { Remove-Item -LiteralPath $demoReport }
    $arguments = "-batchmode $Extra `"--data=$Data`" -logFile `"$results/$Name.log`""
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $script:playerStarts++
    if (!$process.WaitForExit(90000)) { $process.Kill(); throw "Recovery launch timed out: $Name" }
    if ($process.ExitCode -ne $ExpectedCode) { throw "Unexpected exit code in $Name. See $results/$Name.log" }
    if (!(Test-Path "$Data/shutdown-check.txt")) { throw "Shutdown verification missing in $Name" }
    if ($ExpectedVersion) {
        $result = Get-Content "$Data/demo-report.json" -Raw | ConvertFrom-Json
        if (!$result.passed -or $result.version -ne $ExpectedVersion -or $result.checks.Count -lt 62) { throw "Wrong recovered version in $Name" }
        Copy-Item "$Data/demo-report.json" "$results/$Name-report.json"
    }
}
foreach ($scenario in @('FirstFailure','FirstCancelled')) {
    $scenarioData = Join-Path $results $scenario
    $fault = if ($scenario -eq 'FirstFailure') { '--fail-before-ready' } else { '--cancel-before-ready' }
    Invoke-RecoveryLaunch "$scenario-initial" $scenarioData "--self-test $fault" 1 ''
    $failure = Get-Content "$scenarioData/failure.txt" -Raw
    if ($scenario -eq 'FirstFailure' -and !$failure.Contains('Requested test fault')) { throw 'Wrong first-boot fault' }
    if ($scenario -eq 'FirstCancelled' -and !$failure.Contains('CanceledException')) { throw 'Wrong first-boot cancellation' }
    Invoke-RecoveryLaunch "$scenario-retry" $scenarioData '--self-test' 0 '1.0.0'
}
$cancelledCandidate = Join-Path $results 'CancelledCandidate'
Invoke-RecoveryLaunch 'Candidate-stage' $cancelledCandidate '--self-test --stage-update' 0 '1.0.0'
Invoke-RecoveryLaunch 'Candidate-cancel' $cancelledCandidate '--self-test --cancel-before-ready' 1 ''
if (!(Get-Content "$cancelledCandidate/failure.txt" -Raw).Contains('CanceledException') -or
    !(Select-String -Path "$results/Candidate-cancel.log" -SimpleMatch 'update.boot.cancelled' -Quiet)) { throw 'Candidate cancellation was not preserved after cleanup' }
Invoke-RecoveryLaunch 'Candidate-retry' $cancelledCandidate '--self-test' 0 '1.1.0'

# Damage only fixtures created by this suite. V2 also loses its installed manifest.
foreach ($scenario in @('FirstFailure','Data')) {
    $scenarioData = Join-Path $results $scenario
    $updateRoot = @(Get-ChildItem "$scenarioData/Updates" -Directory)
    if ($updateRoot.Count -ne 1) { throw 'Ambiguous update fixture' }
    $state = Get-Content (Join-Path $updateRoot[0].FullName 'state.json') -Raw | ConvertFrom-Json
    $installed = Join-Path $updateRoot[0].FullName "versions/$($state.active)"
    if (![IO.Path]::GetFullPath($installed).StartsWith([IO.Path]::GetFullPath($results) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped test directory' }
    $saveHash = (Get-FileHash "$scenarioData/save.json" -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $installed 'gamecontent'), 'damaged test resource')
    if ($scenario -eq 'Data') { [IO.File]::WriteAllText((Join-Path $installed 'manifest.json'), 'damaged test manifest') }
    Invoke-RecoveryLaunch "Repair-$scenario" $scenarioData '--quit-after-ready --exit-on-startup-failure' 0 ''
    if ((Get-FileHash "$scenarioData/save.json" -Algorithm SHA256).Hash -ne $saveHash) { throw 'Repair changed save bytes' }
    $after = Get-Content (Join-Path $updateRoot[0].FullName 'state.json') -Raw | ConvertFrom-Json
    if ($after.active -ne $state.active -or $after.previous -ne $state.previous) { throw 'Repair changed version history' }
    if (!(Select-String -Path "$results/Repair-$scenario.log" -SimpleMatch 'update.repaired' -Quiet)) { throw 'Missing successful repair evidence' }
}
$summary = @{ passed = $true; backend = $Backend; playerStarts = $playerStarts; demoChecks = $report.checks.Count; completedAt = (Get-Date -Format o); playerSha256 = (Get-FileHash $player -Algorithm SHA256).Hash; scenarios = @('V1', 'V2', 'RecoveryV1', 'FailedV2', 'RestoredV1', 'NormalFailure', 'NormalQuit', 'FirstFailureRetry', 'FirstCancellationRetry', 'CandidateCancellationRetry', 'RepairActiveV1', 'RepairActiveV2AndManifest') }
if ($Backend -eq 'HybridCLR') { $summary.gameAssemblySha256 = (Get-FileHash (Join-Path (Split-Path $player) 'GameAssembly.dll') -Algorithm SHA256).Hash }
$summary | ConvertTo-Json | Set-Content (Join-Path $results 'suite-result.json') -Encoding utf8
Write-Output "PASS: $playerStarts launches; V1/V2, rollback, first boot retry, cancellation, active repair, resource leases and awaited quit. Reports: $results"
