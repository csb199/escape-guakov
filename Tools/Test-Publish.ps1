param([switch]$Offline, [string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
$project = if ($ProjectRoot) { [IO.Path]::GetFullPath($ProjectRoot) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
$config = Get-Content "$project/Assets/Resources/game-config.json" -Raw | ConvertFrom-Json
$baseline = Join-Path $project "Builds/ClientBaselines/$($config.clientBuild)"
$results = Join-Path $project ('TestResults/Publish-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $results | Out-Null
$output = Join-Path $results 'Release'
$player = Join-Path $project 'Builds/HybridCLR/EnergyCollector.exe'
$native = Join-Path $project 'Builds/HybridCLR/GameAssembly.dll'
$beforePlayer = (Get-FileHash $player).Hash
$beforeNative = (Get-FileHash $native).Hash
& "$PSScriptRoot/Publish-Release.ps1" -Baseline $baseline -Version '1.2.0' -Sequence 3 -Output $output -SigningKey "$project/LocalOnly/demo-signing-key.xml" -DemoVariant DEMO_V3 -Offline:$Offline -ProjectRoot $project
Copy-Item "$project/Publish.log" "$results/publish.log"
if ((Get-FileHash $player).Hash -ne $beforePlayer -or (Get-FileHash $native).Hash -ne $beforeNative) { throw 'Publishing modified the frozen player' }

$data = Join-Path $results 'Data'
foreach ($version in @(1,2,3)) {
    if (Test-Path "$data/shutdown-check.txt") { Remove-Item -LiteralPath "$data/shutdown-check.txt" }
    $extra = switch ($version) { 1 { '--stage-update' } 2 { '' } 3 { "`"--update-source=$output`"" } }
    $arguments = "-batchmode --self-test $extra `"--data=$data`" -logFile `"$results/player-v$version.log`""
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(90000)) { $process.Kill(); throw "V$version timed out" }
    if ($process.ExitCode -ne 0) { throw "V$version failed; see $results" }
    $report = Get-Content "$data/demo-report.json" -Raw | ConvertFrom-Json
    $expected = @('','1.0.0','1.1.0','1.2.0')[$version]
    if (!$report.passed -or $report.version -ne $expected -or !$report.codeFlavor.StartsWith("V$version /")) { throw 'Release upgrade mismatch' }
    if (!$report.sceneAsset.Contains("/HotScenes/V$version/")) { throw 'Versioned scene did not come from the expected resource package' }
    if ($report.checks.Count -lt 62 -or $report.checks -notcontains 'resource shutdown drains active and queued cache trim operations') { throw 'Missing service regression coverage' }
    if (!(Test-Path "$data/shutdown-check.txt")) { throw 'Shutdown did not drain all services' }
    Copy-Item "$data/demo-report.json" "$results/v$version-report.json"
}

$v2Envelope = Get-Content "$baseline/latest-manifest.json" -Raw | ConvertFrom-Json
$v3Envelope = Get-Content "$output/manifest.json" -Raw | ConvertFrom-Json
$v2 = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($v2Envelope.payload)) | ConvertFrom-Json
$v3 = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($v3Envelope.payload)) | ConvertFrom-Json
foreach ($sceneBundle in @('scene-lobby','scene-arena')) {
    $oldFile = $v2.files | Where-Object name -eq $sceneBundle
    $newFile = $v3.files | Where-Object name -eq $sceneBundle
    if (!$oldFile -or !$newFile -or $oldFile.sha256 -eq $newFile.sha256) { throw 'Independent V3 publication did not update authored scene content' }
}

# Fault injection changes only a copy of the baseline, never source or the shipped baseline.
$badBaseline = Join-Path $results 'RejectedBaseline'
Copy-Item $baseline $badBaseline -Recurse
$bad = Get-Content "$badBaseline/baseline.json" -Raw | ConvertFrom-Json
$bad.aotAssemblies[0].sha256 = '0' * 64
$bad | ConvertTo-Json -Depth 20 | Set-Content "$badBaseline/baseline.json" -Encoding utf8
$rejected = $false
try {
    & "$PSScriptRoot/Publish-Release.ps1" -Baseline $badBaseline -Version '1.3.0' -Sequence 4 -Output "$results/RejectedRelease" -SigningKey "$project/LocalOnly/demo-signing-key.xml" -DemoVariant DEMO_V3 -Offline:$Offline -ProjectRoot $project
} catch {
    if (!(Select-String -Path "$project/Publish.log" -SimpleMatch 'Frozen client file changed' -Quiet)) { throw }
    $rejected = $true
}
Copy-Item "$project/Publish.log" "$results/rejected-publish.log"
if (!$rejected -or (Test-Path "$results/RejectedRelease")) { throw 'Incompatible baseline was published' }
if ((Get-FileHash $player).Hash -ne $beforePlayer -or (Get-FileHash $native).Hash -ne $beforeNative) { throw 'Frozen player changed during verification' }
@{ passed = $true; clientBuild = $config.clientBuild; playerStarts = 3; demoChecks = $report.checks.Count;
   version = '1.2.0'; codeFlavor = $report.codeFlavor; playerSha256 = $beforePlayer; gameAssemblySha256 = $beforeNative;
   incompatibleBaselineRejected = $rejected; sceneBundlesChanged = $true; sceneAsset = $report.sceneAsset; completedAt = (Get-Date -Format o) } |
   ConvertTo-Json | Set-Content "$results/suite-result.json" -Encoding utf8
Write-Output "PASS: standalone V3 publication, three-version upgrade, unchanged native player, incompatible baseline rejection. Reports: $results"
