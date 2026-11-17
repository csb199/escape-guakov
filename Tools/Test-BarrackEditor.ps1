param([string]$Unity = 'E:\Unity\6000.3.27f2\Editor\Unity.exe', [switch]$Offline)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$working = $project
if ($Offline) {
    $working = Join-Path $project 'LocalOnly/OfflineProject'
    New-Item -ItemType Directory -Path "$working/Assets", "$working/Packages", "$working/ProjectSettings" -Force | Out-Null
    & "$PSScriptRoot/Sync-GameSource.ps1" -Project $project
    foreach ($folder in @('Assets/Resources', 'Assets/HybridCLR', 'Packages', 'ProjectSettings')) {
        & robocopy (Join-Path $project $folder) (Join-Path $working $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Failed to synchronize $folder" }
    }
    foreach ($package in Get-ChildItem -LiteralPath "$project/Library/PackageCache" -Directory | Where-Object { $_.Name -like '*@*' }) {
        $packageName = ($package.Name -split '@')[0]
        if (!(Test-Path -LiteralPath "$working/Packages/$packageName/package.json")) {
            & robocopy $package.FullName "$working/Packages/$packageName" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Failed to synchronize $packageName" }
        }
    }
    $global:LASTEXITCODE = 0
}
$output = Join-Path $working 'LocalOnly/Barrack/live'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$started = Get-Date
$arguments = "-batchmode -force-d3d11 -screen-width 1280 -screen-height 720 -projectPath `"$working`" -executeMethod Game.Editor.Barrack.BarrackFullValidation.Run -logFile `"$output/full-validation.log`""
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(1200000)) { $process.Kill(); throw 'Barrack Play Mode validation timed out.' }
if ($process.ExitCode -ne 0) { throw "Barrack validation failed. See $output/full-validation.log" }
$result = Get-Item -LiteralPath "$output/full-validation.txt"
if ($result.LastWriteTime -lt $started -or (Get-Content -LiteralPath $result.FullName -Raw) -notmatch '^PASS') { throw 'Missing fresh validation result.' }
Get-Content -LiteralPath $result.FullName
