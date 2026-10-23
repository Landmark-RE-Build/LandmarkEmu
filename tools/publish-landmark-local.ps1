# Builds a release-shaped LandmarkLocal folder: app + Auth + World + LaunchPad + data/.
# Self-contained win-x64 so players need no separate .NET / ASP.NET install (grandma path).
# Usage (from repo root):  powershell -File tools/publish-landmark-local.ps1
param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/LandmarkLocal"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

if ([System.IO.Path]::IsPathRooted($Output)) {
    $out = $Output
} else {
    $out = Join-Path $root $Output
}
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path $out -Force | Out-Null

# No PDBs / debug symbols in the player zip. Bundle the runtime into each app folder.
$publishProps = @(
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

function Publish-Project([string]$Project, [string]$Dest, [string[]]$ExtraArgs = @()) {
    Write-Host "Publishing $Project → $Dest (self-contained win-x64)"
    & dotnet publish $Project -c $Configuration -o $Dest @publishProps @ExtraArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $Project" }
}

function Remove-DebugJunk([string]$Dir) {
    if (-not (Test-Path $Dir)) { return }
    # PDBs are for developers only.
    Get-ChildItem -Path $Dir -Recurse -Filter *.pdb -File | Remove-Item -Force
    # XML next to a same-named dll/exe is usually .NET XML doc, not config (we ship JSON).
    Get-ChildItem -Path $Dir -Recurse -Filter *.xml -File | Where-Object {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
        $dir = $_.DirectoryName
        (Test-Path (Join-Path $dir "$base.dll")) -or (Test-Path (Join-Path $dir "$base.exe"))
    } | Remove-Item -Force
}

Publish-Project "LandmarkLocal/LandmarkLocal.csproj" $out

$authOut = Join-Path $out "servers/AuthServer"
Publish-Project "LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj" $authOut
Copy-Item (Join-Path $authOut "AuthServer.example.json") (Join-Path $authOut "AuthServer.json") -Force

$worldOut = Join-Path $out "servers/WorldServer"
Publish-Project "LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj" $worldOut
Copy-Item (Join-Path $worldOut "WorldServer.example.json") (Join-Path $worldOut "WorldServer.json") -Force

$lpOut = Join-Path $out "LaunchPad"
Publish-Project "LandmarkEmulator.LaunchPad/LandmarkEmulator.LaunchPad.csproj" $lpOut @("-p:_IsPublishing=true")

if (Test-Path (Join-Path $root "data")) {
    Copy-Item -Recurse (Join-Path $root "data") (Join-Path $out "data")
}

New-Item -ItemType Directory -Path (Join-Path $out "patches") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $out "server-data/gamedata") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $out "server-data/worlds/main") -Force | Out-Null

Copy-Item (Join-Path $root "docs/landmark-local.md") (Join-Path $out "README-LandmarkLocal.md") -Force

Write-Host "Stripping PDB / doc XML from publish output…"
Remove-DebugJunk $out

Write-Host "Done: $out"
Write-Host "Zip that folder for release. MariaDB and terrain heights download on first Start."
Write-Host "Self-contained: no separate .NET / ASP.NET install needed on the player PC."
