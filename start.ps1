# Optional Windows helper for the Docker setup: run start.bat. Everything it does can be done by hand with
# docker compose (README, docs/docker.md); it only saves typing.
#
#   start.bat            first run: asks a few questions and writes .env; then (every run) docker compose up
#   start.bat -Stop      docker compose down (all data stays)
#   start.bat -Update    rebuilds the servers from this folder's code (after downloading a new version)
#   start.bat -Logs      the world server's console (Ctrl+C to leave)
#   start.bat -Setup     asks the first-run questions again
#
# Settings: .env (made from .env.example). Extra worlds: docker\worlds\*.env (see extra-world.env.example).
param([switch]$Stop, [switch]$Update, [switch]$Logs, [switch]$Setup)
$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
$envFile = Join-Path $root '.env'
$worlds = Join-Path $root 'docker\worlds'

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }

# .env: read and change one value, keeping the notes.
function Read-Env {
    $values = @{}
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
        }
    }
    return $values
}
function Set-EnvValue([string]$key, [string]$value) {
    $found = $false
    $lines = @(Get-Content $envFile) | ForEach-Object { if ($_ -match "^\s*$key=") { $found = $true; "$key=$value" } else { $_ } }
    if (-not $found) { $lines += "$key=$value" }
    Set-Content $envFile $lines -Encoding ASCII
}

function Test-GameFolder([string]$path) {
    return $path -and (Test-Path (Join-Path $path 'Landmark64.exe')) -and (Test-Path (Join-Path $path 'Resources'))
}
function New-Password { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ }) }

# The project (docker-compose.yml), and each extra world in docker\worlds.
function Compose { & docker compose --project-directory $root -f (Join-Path $root 'docker-compose.yml') @args }
function Compose-World([string]$worldEnv) {
    $name = [IO.Path]::GetFileNameWithoutExtension($worldEnv)
    & docker compose --env-file $envFile --env-file $worldEnv -p "landmarkemu-world-$name" -f (Join-Path $root 'docker\world\docker-compose.yml') @args
}
function Extra-Worlds { Get-ChildItem $worlds -Filter '*.env' -ErrorAction SilentlyContinue }

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

# ---- First run: the questions .env needs ----------------------------------------------------------------------------
function Invoke-Setup {
    Say "`n=== LandmarkEmu first-time setup ===`n" 'Cyan'
    if (-not (Test-Path $envFile)) { Copy-Item (Join-Path $root '.env.example') $envFile }
    $settings = Read-Env

    $game = $settings['GAME_DIR'] -replace '/', '\'
    while (-not (Test-GameFolder $game)) {
        $game = (Read-Host 'Your Landmark folder (the one with Landmark64.exe, e.g. ...\steamapps\common\Landmark)').Trim('" ')
        if (-not (Test-GameFolder $game)) { Say 'Landmark64.exe and Resources were not found there.' 'Yellow' }
    }
    Set-EnvValue 'GAME_DIR' ($game -replace '\\', '/')

    if (-not $settings['DB_ROOT_PASSWORD']) { Set-EnvValue 'DB_ROOT_PASSWORD' (New-Password) }
    if (-not $settings['DB_PASSWORD']) { Set-EnvValue 'DB_PASSWORD' (New-Password) }

    $admin = (Read-Host "Your account name, to be an admin on your world (empty: $(if ($settings['ADMIN_ACCOUNT']) { "keep $($settings['ADMIN_ACCOUNT'])" } else { 'none' }); you create the account in LaunchPad)").Trim()
    # Saved as account:<name>: a bare name also matches a character of that name, and when an account and another
    # player's character share it the world makes nobody admin (docs/admin.md). An entry that already has an
    # account: / character: prefix is kept as typed.
    if ($admin) {
        if ($admin -notmatch '^(?i)(account|character)\s*:') { $admin = "account:$admin" }
        Set-EnvValue 'ADMIN_ACCOUNT' $admin
    }

    $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { "$($_.IPAddress) ($($_.InterfaceAlias))" })
    Say "`nPlayers on other PCs need an address that reaches this PC. This PC has:" 'Cyan'
    $addresses | ForEach-Object { Say "  $_" }
    $current = $settings['PUBLIC_HOST']
    $answer = (Read-Host "Address for players (empty: $(if ($current) { "keep $current" } else { 'this PC only' }))").Trim()
    if ($answer) { Set-EnvValue 'PUBLIC_HOST' $answer }
    Say "Settings saved in .env (edit it any time, then run start.bat again).`n" 'Green'
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
    foreach ($w in Extra-Worlds) { Compose-World $w.FullName down }
    Compose down
    Say 'Servers stopped (all data kept).' 'Green'
    return
}
if ($Logs) { Compose logs -f --tail 50 world; return }

$firstRun = $Setup -or -not (Test-Path $envFile)
if ($firstRun) { Invoke-Setup }

$n = Read-Env
function Port([string]$key, [string]$default) { if ($n[$key]) { $n[$key] } else { $default } }
$net = @{
    Host = $(if ($n['PUBLIC_HOST']) { $n['PUBLIC_HOST'] } else { '127.0.0.1' })
    Login = Port 'LOGIN_PORT' '20042'; Account = Port 'ACCOUNT_PORT' '5000'; World = Port 'WORLD_PORT' '19000'
    WebPages = Port 'WEB_PAGES_PORT' '19001'; Website = Port 'WEBSITE_PORT' '8080'
}
if ($n['PUBLIC_HOST']) { Open-Firewall $net }

# docker compose up: builds the images the first time (or with -Update), then starts everything.
$up = @('up', '-d') + $(if ($firstRun -or $Update) { @('--build') } else { @() })
if ($firstRun -or $Update) { Say 'Building the servers (the first time takes several minutes)...' }
Compose @up
if ($LASTEXITCODE -ne 0) { throw 'The servers did not start (see the messages above).' }
foreach ($w in Extra-Worlds) {
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
Say "  Stop: start.bat -Stop     Console: start.bat -Logs     Settings: .env"
