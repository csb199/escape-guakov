param(
    [string]$Unity = 'E:\Unity\6000.3.27f2\Editor\Unity.exe',
    [string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $project 'LocalOnly/Barrack/continuous'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$started = Get-Date
$arguments = "-batchmode -force-d3d11 -screen-width 640 -screen-height 360 -projectPath `"$project`" -executeMethod Game.Editor.Barrack.BarrackSequenceValidation.Run -logFile `"$output/unity.log`""
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(1140000)) { $process.Kill(); throw 'Barrack sequence collection timed out.' }
if ($process.ExitCode -ne 0) { throw "Sequence collection failed. See $output/unity.log" }
$result = Get-Item -LiteralPath "$output/status.txt"
if ($result.LastWriteTime -lt $started -or (Get-Content -LiteralPath $result.FullName -Raw) -notmatch '^COLLECTED 1684') { throw 'Missing fresh sequence collection result.' }
& $Python (Join-Path $PSScriptRoot 'Analyze-BarrackSequence.py') --root $project
if ($LASTEXITCODE -ne 0) { throw 'Quantitative analysis failed.' }
$metrics = Get-Content -LiteralPath "$output/metrics.json" -Raw | ConvertFrom-Json
Write-Output "Collection complete. Visual/source acceptance: $($metrics.acceptance.overall). See $output/metrics.json"
# A successful collection is not an assertion of original-game equivalence.
