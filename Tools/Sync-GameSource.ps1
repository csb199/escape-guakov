param([Parameter(Mandatory=$true)][string]$Project)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Project)
$source = Join-Path $root 'Assets/Game'
$destination = Join-Path $root 'LocalOnly/OfflineProject/Assets/Game'
if (!(Test-Path -LiteralPath "$source/Bootstrap/Bootstrap.cs") -or
    !(Test-Path -LiteralPath "$root/LocalOnly/OfflineProject/Assets")) {
    throw 'Expected framework source and offline project before synchronization.'
}
# Only this disposable mirror is synchronized. /MIR removes stale scripts after source moves.
& robocopy $source $destination /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Offline Game source synchronization failed: $LASTEXITCODE" }
$global:LASTEXITCODE = 0
