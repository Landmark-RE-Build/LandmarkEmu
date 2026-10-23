# Resolve the next LandmarkLocal semver from existing git tags (landmark-local-v*).
# Usage:
#   powershell -File tools/landmark-local-next-version.ps1 -Bump patch
#   … -Bump minor|major
# Prints: VERSION=x.y.z  and  TAG=landmark-local-vx.y.z  (one per line; also stdout version only via -VersionOnly)
param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",
    [string]$Prefix = "landmark-local-v",
    [string]$DefaultVersion = "0.1.0",
    [switch]$VersionOnly
)

$ErrorActionPreference = "Stop"

function Get-SemVerParts([string]$Text) {
    if ($Text -notmatch '^\d+\.\d+\.\d+$') { return $null }
    $p = $Text.Split('.') | ForEach-Object { [int]$_ }
    return @{ Major = $p[0]; Minor = $p[1]; Patch = $p[2]; Text = $Text }
}

$tags = @(git tag -l "$Prefix*" 2>$null)
$versions = @(foreach ($t in $tags) {
    if (-not $t.StartsWith($Prefix)) { continue }
    $v = Get-SemVerParts ($t.Substring($Prefix.Length))
    if ($null -ne $v) { $v }
})

if ($versions.Count -eq 0) {
    # No tags yet: first release is 0.1.0 (patch/minor) or 1.0.0 (major).
    $current = Get-SemVerParts "0.0.0"
    $previousText = "(none)"
} else {
    $current = $versions |
        Sort-Object { $_.Major }, { $_.Minor }, { $_.Patch } |
        Select-Object -Last 1
    $previousText = $current.Text
}

switch ($Bump) {
    "major" { $next = "{0}.0.0" -f ($current.Major + 1) }
    "minor" { $next = "{0}.{1}.0" -f $current.Major, ($current.Minor + 1) }
    "patch" {
        if ($versions.Count -eq 0) { $next = $DefaultVersion }
        else { $next = "{0}.{1}.{2}" -f $current.Major, $current.Minor, ($current.Patch + 1) }
    }
    default { throw "Unknown bump: $Bump" }
}

$tag = "$Prefix$next"
if ($VersionOnly) {
    Write-Output $next
} else {
    Write-Output "VERSION=$next"
    Write-Output "TAG=$tag"
    Write-Output "PREVIOUS=$previousText"
    Write-Output "BUMP=$Bump"
}
