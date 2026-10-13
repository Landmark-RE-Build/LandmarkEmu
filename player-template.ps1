# LandmarkEmu player launcher - the body of "Play LandmarkEmu.bat", which start.bat writes for the host to share.
# start.bat puts launcher-common.ps1 above this and the host's address in place of __SERVER_ADDRESS__; the result is
# one self-contained file. Players need only Landmark (the 0.589 client) and Windows - no .NET, MariaDB or project.
#
# First run: asks for the Landmark folder and an account name / password, and saves them (without the password) in
# %LOCALAPPDATA%\LandmarkEmu\play-<address>.json. Every run: points the game at the server, creates the account there
# the first time, logs in and starts Landmark. "Play LandmarkEmu.bat -Setup" asks again.
$ErrorActionPreference = 'Stop'
$serverAddress = '__SERVER_ADDRESS__'
$setup = "$env:LMEMU_ARGS" -match '(^|\s)[-/]Setup(\s|$)'

$settingsDir = Join-Path $env:LOCALAPPDATA 'LandmarkEmu'
New-Item -ItemType Directory -Force $settingsDir | Out-Null
$settingsFile = Join-Path $settingsDir ("play-" + ($serverAddress -replace '[^A-Za-z0-9.-]', '_') + '.json')

function Invoke-PlaySetup {
    Say "`n=== LandmarkEmu: join the server at $serverAddress ===`n" 'Cyan'
    $game = Select-GameFolder ''
    $user, $password = Read-Account '' ''
    $settings = [pscustomobject][ordered]@{ GamePath = $game; ServerAddress = $serverAddress; Account = $user; Password = $password; Created = $false }
    Save-Settings $settings $settingsFile   # the password is never saved; the first login stores a derived key
    Say "Saved on this PC ($settingsFile). Run the file with -Setup to change the folder or account.`n" 'Green'
    return $settings
}

$settings = if (-not $setup -and (Test-Path $settingsFile)) { Get-Content $settingsFile -Raw | ConvertFrom-Json }
            else { Invoke-PlaySetup | Select-Object -Last 1 }
if (-not (Test-GameFolder $settings.GamePath)) { throw "The game is no longer at '$($settings.GamePath)'. Run the file with -Setup." }

Set-ClientConfig $settings.GamePath $serverAddress
Say "Connecting to $serverAddress..."
Connect-AndPlay $settings $settingsFile $serverAddress '"Play LandmarkEmu.bat" -Setup'
