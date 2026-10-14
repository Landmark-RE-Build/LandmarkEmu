# LandmarkEmu servers, in Docker: run start.bat.
#
#   start.bat            first run: sets everything up, then starts the servers; later runs: starts them
#   start.bat -Stop      stops the servers (all data stays)
#   start.bat -Update    rebuilds the servers from this folder's code (after downloading a new version)
#   start.bat -Logs      shows the world server's console (Ctrl+C to leave)
#   start.bat -Setup     runs the first-time questions again
#
# Settings:
#   network.env              addresses and ports: the one file to edit for networking (made from network.env.example)
#   docker\auth\.env         passwords and folders (made on the first run; rarely edited)
#   docker\worlds\*.env      one file per world (main.env is made on the first run)
# Players use LaunchPad (docs\launchpad.md) and add this server by its address.
param([switch]$Stop, [switch]$Update, [switch]$Logs, [switch]$Setup)
$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$docker   = Join-Path $root 'docker'
$network  = Join-Path $root 'network.env'
$authEnv  = Join-Path $docker 'auth\.env'
$worlds   = Join-Path $docker 'worlds'
$gameData = Join-Path $root 'gameData'

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }

# KEY=VALUE files (network.env, .env): read and change one value, keeping the notes.
function Read-Env([string]$file) {
    $values = @{}
    if (Test-Path $file) {
        foreach ($line in Get-Content $file) {
            if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
        }
    }
    return $values
}
function Set-EnvValue([string]$file, [string]$key, [string]$value) {
    $lines = @(Get-Content $file)
    $found = $false
    $lines = $lines | ForEach-Object { if ($_ -match "^\s*$key=") { $found = $true; "$key=$value" } else { $_ } }
    if (-not $found) { $lines += "$key=$value" }
    Set-Content $file $lines -Encoding ASCII
}
function Path-ForDocker([string]$path) { return ($path -replace '\\', '/') }

function Test-GameFolder([string]$path) {
    return $path -and (Test-Path (Join-Path $path 'Landmark64.exe')) -and (Test-Path (Join-Path $path 'Resources'))
}

function New-Password { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ }) }

# The compose command for the auth stack or one world, with the settings files in order (later ones win).
function Compose-Auth { & docker compose --env-file $network --env-file $authEnv -p landmarkemu-auth -f (Join-Path $docker 'auth\docker-compose.yml') @args }
function Compose-World([string]$worldEnv) {
    $name = [IO.Path]::GetFileNameWithoutExtension($worldEnv)
    & docker compose --env-file $network --env-file $authEnv --env-file $worldEnv -p "landmarkemu-world-$name" -f (Join-Path $docker 'world\docker-compose.yml') @args
}
function World-Files { Get-ChildItem $worlds -Filter '*.env' | Where-Object { $_.Name -notlike '*.example' } }

# ---- Docker Desktop -------------------------------------------------------------------------------------------------
function Start-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker Desktop is not installed. Install it from https://www.docker.com/products/docker-desktop/ and run start.bat again.'
    }
    & docker info *> $null
    if ($LASTEXITCODE -eq 0) { return }
    $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $desktop)) { throw 'Docker is installed but not running. Start Docker Desktop and run start.bat again.' }
    Say 'Starting Docker Desktop (the first time can take a few minutes)...'
    Start-Process $desktop
    $deadline = (Get-Date).AddMinutes(5)
    do { Start-Sleep -Seconds 3; & docker info *> $null } while ($LASTEXITCODE -ne 0 -and (Get-Date) -lt $deadline)
    if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop did not start within 5 minutes. Start it by hand, then run start.bat again.' }
}

# ---- First-time setup -----------------------------------------------------------------------------------------------
function Invoke-Setup {
    Say "`n=== LandmarkEmu first-time setup ===`n" 'Cyan'

    # 1. The game (the servers read its data; nothing in it is changed).
    $settings = Read-Env $authEnv
    $game = $settings['GAME_DIR'] -replace '/', '\'
    while (-not (Test-GameFolder $game)) {
        $game = (Read-Host 'Your Landmark folder (the one with Landmark64.exe, e.g. ...\steamapps\common\Landmark)').Trim('" ')
        if (-not (Test-GameFolder $game)) { Say 'Landmark64.exe and Resources were not found there.' 'Yellow' }
    }

    # 2. gameData: the game's own data tables and English texts, copied from the install, plus the project's data.
    New-Item -ItemType Directory -Force $gameData | Out-Null
    Copy-Item (Join-Path $game 'Resources\*.txt') $gameData -Force
    $locale = Join-Path $game 'Locale\en_us_data.dat'
    if (Test-Path $locale) { Copy-Item $locale $gameData -Force } else { Say 'Locale\en_us_data.dat not found: names will be missing.' 'Yellow' }
    Copy-Item (Join-Path $root 'data\*') $gameData -Force
    # Ground heights (trees, creature footing, starting build sites): LandmarkEmu-terrain-heights.zip from the
    # project's GitHub release, put next to start.bat. Without it those stay off.
    $heights = Join-Path $root 'LandmarkEmu-terrain-heights.zip'
    if (Test-Path $heights) {
        Say 'Unpacking terrain heights...'
        Expand-Archive $heights -DestinationPath $gameData -Force
    } else {
        Say 'No LandmarkEmu-terrain-heights.zip next to start.bat: trees, ground checks and starting build sites will be off (see README).' 'Yellow'
    }
    Say "gameData ready ($((Get-ChildItem $gameData).Count) files)." 'Green'

    # 3. Settings files.
    if (-not (Test-Path $network)) { Copy-Item (Join-Path $root 'network.env.example') $network }
    if (-not (Test-Path $authEnv)) {
        Copy-Item (Join-Path $docker 'auth\.env.example') $authEnv
        Set-EnvValue $authEnv 'DB_ROOT_PASSWORD' (New-Password)
        Set-EnvValue $authEnv 'DB_PASSWORD' (New-Password)
    }
    Set-EnvValue $authEnv 'GAME_DIR' (Path-ForDocker $game)
    Set-EnvValue $authEnv 'GAME_DATA_DIR' (Path-ForDocker $gameData)
    $main = Join-Path $worlds 'main.env'
    if (-not (World-Files)) {
        Copy-Item (Join-Path $worlds 'main.env.example') $main
        Set-EnvValue $main 'WORLD_DATA_DIR' (Path-ForDocker (Join-Path $root 'server-data\worlds\main'))
        $admin = (Read-Host 'Your account name, to be an admin on your world (empty: none; you create the account in LaunchPad)').Trim()
        if ($admin) { Set-EnvValue $main 'ADMIN_ACCOUNT' $admin }
    }

    # 4. Who can join.
    $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { "$($_.IPAddress) ($($_.InterfaceAlias))" })
    Say "`nPlayers on other PCs need an address that reaches this PC. This PC has:" 'Cyan'
    $addresses | ForEach-Object { Say "  $_" }
    $current = (Read-Env $network)['PUBLIC_HOST']
    $answer = (Read-Host "Address for players (empty: this PC only$(if ($current) { "; now $current" }))").Trim()
    if ($answer -or -not $current) { Set-EnvValue $network 'PUBLIC_HOST' $answer }
    Say "Settings saved: network.env (addresses, ports), docker\auth\.env (passwords, folders), docker\worlds\main.env.`n" 'Green'
}

# ---- Firewall (only when hosting for other PCs) ---------------------------------------------------------------------
function Open-Firewall($net) {
    $rule = & netsh advfirewall firewall show rule name=LandmarkEmu-UDP 2>$null
    if ($LASTEXITCODE -eq 0 -and ($rule -match 'LandmarkEmu-UDP')) { return }
    $yes = Read-Host "Open the server ports in Windows Firewall (UDP $($net.Login), $($net.World); TCP $($net.Account), $($net.WebPages), $($net.Website))? Windows will ask for permission [Y/n]"
    if ($yes -match '^[nN]') { return }
    $rules = "netsh advfirewall firewall add rule name=LandmarkEmu-UDP dir=in action=allow protocol=UDP localport=$($net.Login),$($net.World); " +
             "netsh advfirewall firewall add rule name=LandmarkEmu-TCP dir=in action=allow protocol=TCP localport=$($net.Account),$($net.WebPages),$($net.Website)"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -Command $rules"
}

# ---------------------------------------------------------------------------------------------------------------------
Start-Docker

if ($Stop) {
    foreach ($w in World-Files) { Compose-World $w.FullName down }
    Compose-Auth down
    Say 'Servers stopped (all data kept).' 'Green'
    return
}
if ($Logs) {
    $w = World-Files | Select-Object -First 1
    Compose-World $w.FullName logs -f --tail 50
    return
}

$firstRun = $Setup -or -not (Test-Path $authEnv) -or -not (Test-Path $network) -or -not (World-Files)
if ($firstRun) { Invoke-Setup }

$n = Read-Env $network
$net = @{
    Host = $(if ($n['PUBLIC_HOST']) { $n['PUBLIC_HOST'] } else { '127.0.0.1' })
    Login = $(if ($n['LOGIN_PORT']) { $n['LOGIN_PORT'] } else { '20042' }); Account = $(if ($n['ACCOUNT_PORT']) { $n['ACCOUNT_PORT'] } else { '5000' })
    World = $(if ($n['WORLD_PORT']) { $n['WORLD_PORT'] } else { '19000' }); WebPages = $(if ($n['WEB_PAGES_PORT']) { $n['WEB_PAGES_PORT'] } else { '19001' })
    Website = $(if ($n['WEBSITE_PORT']) { $n['WEBSITE_PORT'] } else { '8080' })
}
# The account website link LaunchPad shows, unless set in docker\auth\.env.
if (-not (Read-Env $authEnv)['WEBSITE_URL']) { $env:WEBSITE_URL = "http://$($net.Host):$($net.Website)/" }
if ($n['PUBLIC_HOST']) { Open-Firewall $net }

# Build (first run, or -Update) and start: the auth stack first, then every world.
$up = @('up', '-d') + $(if ($firstRun -or $Update) { @('--build') } else { @() })
if ($firstRun -or $Update) { Say 'Building the servers (the first time takes several minutes)...' }
Compose-Auth @up
if ($LASTEXITCODE -ne 0) { throw 'The auth stack did not start (see the messages above).' }
foreach ($w in World-Files) {
    Compose-World $w.FullName @up
    if ($LASTEXITCODE -ne 0) { throw "The world $($w.BaseName) did not start (see the messages above)." }
}

# Ready when the account service lists a world as up (each world registers itself after loading).
Say 'Waiting for the world server to load...'
$deadline = (Get-Date).AddMinutes(4)
while ($true) {
    $info = & curl.exe -s --max-time 3 "http://127.0.0.1:$($net.Account)/info"
    if ($info -match '"up":true') { break }
    if ((Get-Date) -gt $deadline) { throw 'No world is up after 4 minutes: run start.bat -Logs to see why.' }
    Start-Sleep -Seconds 3
}

Say "`nLandmarkEmu is running." 'Green'
Say "  Players: in LaunchPad, Settings > Servers, add the address  $($net.Host)$(if ($net.Account -ne '5000') { ":$($net.Account)" })"
Say "  Account website:  http://$($net.Host):$($net.Website)/"
Say "  Stop: start.bat -Stop     Console: start.bat -Logs     Settings: network.env"
