param(
    [string]$Unity = 'E:\Unity\6000.3.27f2\Editor\Unity.exe',
    [ValidateSet('Prepare','BuildMono','BuildHybrid','Open')][string]$Action = 'BuildMono',
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$working = $project
$extra = ''
if ($Offline) {
    # Isolated source mirror for installations with a broken Unity Package Manager.
    $working = Join-Path $project 'LocalOnly/OfflineProject'
    New-Item -ItemType Directory -Force -Path "$working/Assets", "$working/Packages", "$working/ProjectSettings", "$working/Assets/ThirdParty" | Out-Null
    & "$PSScriptRoot/Sync-GameSource.ps1" -Project $project
    if (Test-Path "$project/Assets/Resources") { Copy-Item "$project/Assets/Resources" "$working/Assets" -Recurse -Force }
    Copy-Item "$project/ProjectSettings/ProjectVersion.txt" "$working/ProjectSettings" -Force
    $hybrid = Join-Path $project 'Packages/com.code-philosophy.hybridclr'
    if (!(Test-Path "$hybrid/package.json")) { throw 'Offline mode requires the bundled HybridCLR package source.' }
    Copy-Item $hybrid "$working/Packages" -Recurse -Force
    New-Item -ItemType Directory -Force -Path "$working/Assets/ThirdParty/HybridCLR" | Out-Null
    foreach ($part in @('Runtime','Editor','Plugins')) { Copy-Item "$hybrid/$part" "$working/Assets/ThirdParty/HybridCLR" -Recurse -Force }
    Copy-Item "$hybrid/LICENSE" "$working/Assets/ThirdParty/HybridCLR" -ErrorAction SilentlyContinue
    $ugui = Join-Path (Split-Path $Unity) 'Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui'
    New-Item -ItemType Directory -Force -Path "$working/Assets/ThirdParty/UGUI" | Out-Null
    foreach ($part in @('Runtime','Editor','LICENSE.md')) { Copy-Item "$ugui/$part" "$working/Assets/ThirdParty/UGUI" -Recurse -Force }
    [IO.File]::WriteAllText("$working/Packages/manifest.json", '{"dependencies":{}}')
    $extra = '-noUpm'
}
$arguments = "-projectPath `"$working`" $extra -logFile `"$project/$Action.log`""
if ($Action -ne 'Open') { $arguments += " -batchmode -nographics -quit -executeMethod Game.Editor.DemoBuilder.$Action" }
$windowStyle = if ($Action -eq 'Open') { 'Normal' } else { 'Hidden' }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -WindowStyle $windowStyle
if ($Action -eq 'Open') { Write-Output "Opened $working"; exit }
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity failed ($($process.ExitCode)); inspect $project/$Action.log" }
if ($Offline) {
    foreach ($folder in @('Builds','Assets/Resources','Assets/StreamingAssets','Assets/HybridCLR','Assets/Game/Scenes','Assets/Game/HotScenes','Assets/Game/Content','LocalOnly')) {
        if ($folder -eq 'LocalOnly') { continue }
        $source = Join-Path $working $folder
        if (Test-Path $source) {
            $parent = Split-Path (Join-Path $project $folder)
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            Copy-Item $source $parent -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Force -Path "$project/LocalOnly" | Out-Null
    Copy-Item "$working/LocalOnly/demo-signing-key.xml" "$project/LocalOnly" -Force
    Copy-Item "$working/ProjectSettings/*" "$project/ProjectSettings" -Force
    # Preserve GUIDs generated for new source assets in the offline mirror.
    $gameRoot = Join-Path $working 'Assets/Game'
    foreach ($meta in Get-ChildItem -LiteralPath $gameRoot -Filter '*.meta' -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($gameRoot, $meta.FullName)
        $destination = Join-Path "$project/Assets/Game" $relative
        if (!(Test-Path -LiteralPath $destination) -and (Test-Path -LiteralPath $destination.Substring(0, $destination.Length - 5))) {
            Copy-Item -LiteralPath $meta.FullName -Destination $destination
        }
    }
}
Write-Output "Unity $Action succeeded: $project"

