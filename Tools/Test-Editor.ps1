param([switch]$Offline, [string]$Unity = 'E:\Unity\6000.3.27f2\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$working = $project
$extra = ''
if ($Offline) {
    $working = Join-Path $project 'LocalOnly/OfflineProject'
    if (!(Test-Path "$working/Assets/ThirdParty/HybridCLR")) { throw 'Build the offline client first.' }
    & "$PSScriptRoot/Sync-GameSource.ps1" -Project $project
    $extra = '-noUpm'
}
$result = Join-Path $project ('TestResults/Editor-' + (Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $result | Out-Null
$arguments = "-batchmode $extra -projectPath `"$working`" -executeMethod Game.Editor.DemoBuilder.PlaySmoke --self-test `"--data=$result`" -logFile `"$result/editor.log`""
$process = Start-Process $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(180000)) { $process.Kill(); throw 'Editor validation timed out.' }
if ($process.ExitCode -ne 0) { throw "Editor validation failed: $result" }
$report = Get-Content "$result/demo-report.json" -Raw | ConvertFrom-Json
if (!$report.passed -or $report.checks.Count -lt 62 -or !(Test-Path "$result/shutdown-check.txt")) { throw 'Incomplete Editor validation' }
Write-Output "PASS: Editor Play Mode, $($report.checks.Count) checks and shutdown: $result"

