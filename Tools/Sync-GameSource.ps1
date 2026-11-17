param([Parameter(Mandatory=$true)][string]$Project, [string]$DestinationProject)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Project)
$source = Join-Path $root 'Assets/Game'
$mirror = if ($DestinationProject) { [IO.Path]::GetFullPath($DestinationProject) } else { Join-Path $root 'LocalOnly/OfflineProject' }
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'LocalOnly')) + [IO.Path]::DirectorySeparatorChar
if ((Test-Path -LiteralPath (Join-Path $root 'LocalOnly')) -and ((Get-Item -LiteralPath (Join-Path $root 'LocalOnly')).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'LocalOnly cannot be a reparse point.' }
if (!$mirror.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Mirror must be inside this project LocalOnly directory.' }
$destination = Join-Path $mirror 'Assets/Game'
for ($ancestor = $mirror; $ancestor -and $ancestor.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase); $ancestor = Split-Path $ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Mirror cannot traverse a reparse point.' }
}
if (Test-Path -LiteralPath $destination) {
    if (Get-ChildItem -LiteralPath $mirror -Recurse -Force -Attributes ReparsePoint) { throw 'Mirror cannot contain reparse points.' }
}
if (!(Test-Path -LiteralPath "$source/Bootstrap/Bootstrap.cs") -or
    !(Test-Path -LiteralPath "$mirror/Assets")) {
    throw 'Expected framework source and offline project before synchronization.'
}
# Only this disposable mirror is synchronized. /MIR removes stale scripts after source moves.
& robocopy $source $destination /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Offline Game source synchronization failed: $LASTEXITCODE" }
$global:LASTEXITCODE = 0
