# LandmarkEmu: play on a server someone else runs. Run play.bat. (The PC that runs the servers uses start.bat.)
#
# Needs only Landmark (the 0.589 client) and Windows — no .NET, MariaDB or build. The first run asks for your
# Landmark folder, the server's address (the host's ServerAddress in their server.ini) and an account name /
# password, and saves them in play-settings.json. Every run: points the game at that server, creates your account
# there the first time, logs in and starts Landmark.
#
#   play.bat            play
#   play.bat -Setup     change the folder, server or account
param([switch]$Setup)
$ErrorActionPreference = 'Stop'

$root         = $PSScriptRoot
$settingsFile = Join-Path $root 'play-settings.json'
. (Join-Path $root 'launcher-common.ps1')

function Invoke-PlaySetup {
    Say "`n=== LandmarkEmu: join a server ===`n" 'Cyan'
    $game = Select-GameFolder ''
    $address = ''
    while (-not $address) { $address = (Read-Host "Server address (ask the host; e.g. 192.168.1.20)").Trim() -replace '^https?://', '' -replace '[:/].*$', '' }
    $user, $password = Read-Account '' ''
    $settings = [pscustomobject][ordered]@{ GamePath = $game; ServerAddress = $address; Account = $user; Password = $password; Created = $false }
    Save-Settings $settings $settingsFile   # the password is never saved; the first login stores a derived key
    Say "Saved in play-settings.json (on this PC only; run play.bat -Setup to change).`n" 'Green'
    return $settings
}

$settings = if (-not $Setup -and (Test-Path $settingsFile)) { Get-Content $settingsFile -Raw | ConvertFrom-Json }
            else { Invoke-PlaySetup | Select-Object -Last 1 }
if (-not (Test-GameFolder $settings.GamePath)) { throw "The game is no longer at '$($settings.GamePath)'. Run play.bat -Setup." }

Set-ClientConfig $settings.GamePath $settings.ServerAddress
Say "Connecting to $($settings.ServerAddress)..."
Connect-AndPlay $settings $settingsFile $settings.ServerAddress 'play.bat -Setup'
