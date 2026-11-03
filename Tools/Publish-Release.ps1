param(
    [Parameter(Mandatory=$true)][string]$Baseline,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][int]$Sequence,
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][string]$SigningKey,
    [string]$PreviousManifest,
    [ValidateSet('','DEMO_V2','DEMO_V3')][string]$DemoVariant = '',
    [string]$Unity = 'C:\Program Files\Unity 2022.3.62f1\Editor\Unity.exe',
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$working = $project
$extra = ''
if ($Offline) {
    $working = Join-Path $project 'LocalOnly/OfflineProject'
    if (!(Test-Path "$working/Assets/ThirdParty/HybridCLR")) { throw 'Build the offline client once before publishing.' }
    & "$PSScriptRoot/Sync-GameSource.ps1" -Project $project
    $extra = '-noUpm'
}
$baselinePath = [IO.Path]::GetFullPath($Baseline)
$outputPath = [IO.Path]::GetFullPath($Output)
$keyPath = [IO.Path]::GetFullPath($SigningKey)
$arguments = "-batchmode -nographics -quit $extra -projectPath `"$working`" -executeMethod Game.Editor.ReleasePublisher.Publish -logFile `"$project/Publish.log`" `"--client-baseline=$baselinePath`" `"--release-output=$outputPath`" `"--release-version=$Version`" --release-sequence=$Sequence `"--signing-key=$keyPath`""
if ($PreviousManifest) { $arguments += " `"--previous-manifest=$([IO.Path]::GetFullPath($PreviousManifest))`"" }
if ($DemoVariant) { $arguments += " --release-define=$DemoVariant" }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw 'Release publication failed; see Publish.log' }
if (!(Test-Path "$outputPath/manifest.json")) { throw 'Published manifest missing' }
Write-Output "Published $Version for frozen baseline: $outputPath"
