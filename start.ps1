# LandmarkEmu launcher for the PC that runs the servers: run start.bat. (Players on other PCs use play.bat.)
#
# First run: asks where Landmark (the 0.589 client) is installed and for an account name / password, then sets
# everything up once — builds the servers, creates the database user, fills gameData from the game's own data tables,
# writes the server configs and creates your account — and saves the answers in launcher-settings.json.
# Every run: applies server.ini (see below), starts the auth and world servers, logs in and starts the game.
#
# server.ini (made on the first run, next to start.bat) holds what a host may want to change, e.g.
#   ServerAddress=192.168.1.20     the address players on OTHER PCs reach this PC on; empty = this PC only
# Edit it and run start.bat again (stop the servers first so they restart with it).
#
#   start.bat               set up (first time) and play
#   start.bat -NoGame       servers only
#   start.bat -Setup        run the first-time setup again (e.g. the game moved)
# Unattended setup: -GamePath <folder> -Account <name> -Password <pw> [-DbRootPassword <pw>]
param([switch]$NoGame, [switch]$Setup, [string]$GamePath, [string]$Account, [string]$Password, [string]$DbRootPassword)
$ErrorActionPreference = 'Stop'
$rootPasswordGiven = $PSBoundParameters.ContainsKey('DbRootPassword')   # an empty one counts

$root         = $PSScriptRoot
$settingsFile = Join-Path $root 'launcher-settings.json'
$serverIni    = Join-Path $root 'server.ini'
$gameData     = Join-Path $root 'gameData'
$dbUser       = 'lmemu_local'
. (Join-Path $root 'launcher-common.ps1')

function Wait-Port([int]$port, [string]$proto, [int]$seconds = 90) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $up = if ($proto -eq 'UDP') { Get-NetUDPEndpoint -LocalPort $port -ErrorAction SilentlyContinue }
              else { Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue }
        if ($up) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Nothing is listening on $proto port $port after $seconds s. Check the server window for errors."
}

function Find-MariaDb {
    $client = Get-ChildItem 'C:\Program Files\MariaDB*\bin\mariadb.exe', 'C:\Program Files\MySQL\*\bin\mysql.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $client) {
        throw 'MariaDB was not found. Install it (https://mariadb.org/download/ or: winget install MariaDB.Server), keep the "MariaDB" Windows service, then run start.bat again.'
    }
    $service = Get-Service MariaDB, MySQL -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $service) { throw 'MariaDB is installed but its Windows service is missing. Re-run the MariaDB installer with "Install as service".' }
    if ($service.Status -ne 'Running') {
        Say "Starting the $($service.Name) service (Windows may ask for permission)..."
        Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -Command Start-Service $($service.Name)"
    }
    return $client.FullName
}

function Find-Build([string]$name) {
    foreach ($config in 'Release', 'Debug') {
        $dir = Get-ChildItem (Join-Path $root "LandmarkEmulator.$name\bin\$config") -Directory -Filter 'net*' -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName "LandmarkEmulator.$name.exe") } | Select-Object -First 1
        if ($dir) { return $dir.FullName }
    }
    return $null
}

function New-Password { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ }) }

# server.ini: "Key=Value" lines, '#' comments. Written with explanations the first time.
function Read-ServerIni {
    if (-not (Test-Path $serverIni)) {
        @(
            '# LandmarkEmu server settings. Edit, then run start.bat again (close the two server windows first).',
            '#',
            '# ServerAddress: the address players on OTHER PCs use to reach this PC, e.g. its network address',
            '# (192.168.1.20; "ipconfig" shows it as IPv4 Address), a VPN address (Tailscale / ZeroTier) or a host name.',
            '# Leave it empty to play on this PC only. Players then run play.bat and enter the same address.',
            '# Logins never send the password (SRP); the game traffic itself is not encrypted, so over the internet a VPN is safer.',
            'ServerAddress=',
            '#',
            '# Admins: more account names (comma-separated) that get the in-game admin menu (type /admin in chat).',
            '# Your own account (the one start.bat logs in with) is always an admin.',
            'Admins='
        ) | Set-Content $serverIni -Encoding UTF8
    }
    $ini = @{}
    foreach ($line in Get-Content $serverIni) {
        if ($line -match '^\s*([A-Za-z]+)\s*=\s*(.*?)\s*$') { $ini[$Matches[1]] = $Matches[2] }
    }
    return $ini
}

# Writes the network part of server.ini into the server configs. This PC only: everything on 127.0.0.1. Hosting:
# listen on every network card, send clients the address as the world server (AuthServer WorldAddress; without it a
# remote client loads the world from its own PC and stops on the loading screen) and as the store pages' address.
function Set-ServerNetwork([string]$address) {
    $listen = if ($address) { '0.0.0.0' } else { '127.0.0.1' }
    $authFile = Join-Path (Find-Build 'AuthServer') 'AuthServer.json'
    $worldFile = Join-Path (Find-Build 'WorldServer') 'WorldServer.json'
    $auth = Get-Content $authFile -Raw | ConvertFrom-Json
    $world = Get-Content $worldFile -Raw | ConvertFrom-Json
    $auth.Network.Host = $listen
    $auth.WebApi.BaseURI = "http://${listen}:5000"
    $auth | Add-Member -Force NoteProperty WorldAddress $address
    $world.Network.Host = $listen
    $world | Add-Member -Force NoteProperty WebPagesHost $listen
    $world | Add-Member -Force NoteProperty WebPagesUrl $(if ($address) { "http://${address}:19001" } else { 'http://127.0.0.1:19001' })
    $auth | ConvertTo-Json -Depth 5 | Set-Content $authFile -Encoding UTF8
    $world | ConvertTo-Json -Depth 5 | Set-Content $worldFile -Encoding UTF8

    if (-not $address) { return }
    # Firewall: login (UDP 20042), world (UDP 19000), account / login service (TCP 5000), store pages (TCP 19001).
    $rule = & netsh advfirewall firewall show rule name=LandmarkEmu-UDP 2>$null
    if ($LASTEXITCODE -ne 0 -or -not ($rule -match 'LandmarkEmu-UDP')) {
        $yes = Read-Host 'Open the server ports in Windows Firewall (UDP 20042, 19000; TCP 5000, 19001)? Windows will ask for permission [Y/n]'
        if ($yes -notmatch '^[nN]') {
            $rules = "netsh advfirewall firewall add rule name=LandmarkEmu-UDP dir=in action=allow protocol=UDP localport=20042,19000; " +
                     "netsh advfirewall firewall add rule name=LandmarkEmu-TCP dir=in action=allow protocol=TCP localport=5000,19001"
            Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -Command $rules"
        }
    }
    Say "Hosting for other PCs at ${address}: players run play.bat and enter ${address}." 'Cyan'
}

# The in-game admin menu (WorldServer.json Admins): this PC's own account plus the names in server.ini.
function Set-ServerAdmins([string]$list, [string]$own) {
    $names = @($own) + @(($list -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) | Select-Object -Unique
    $worldFile = Join-Path (Find-Build 'WorldServer') 'WorldServer.json'
    $world = Get-Content $worldFile -Raw | ConvertFrom-Json
    $world | Add-Member -Force NoteProperty Admins @($names)
    $world | ConvertTo-Json -Depth 5 | Set-Content $worldFile -Encoding UTF8
}

function Invoke-Setup {
    Say "`n=== LandmarkEmu first-time setup ===`n" 'Cyan'

    # 1. The game.
    $game = Select-GameFolder $GamePath

    # 2. Tools the setup needs.
    try { $sdk = (& dotnet --list-sdks) -join "`n" } catch { $sdk = '' }
    if ($sdk -notmatch '(^|\n)(1\d|[2-9]\d)\.') { throw '.NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run start.bat again.' }
    $mariadb = Find-MariaDb

    # 3. Database user (random password, local connections only) and the two databases' rights.
    if ($rootPasswordGiven) { $rootPlain = $DbRootPassword }
    else {
        $rootPw = Read-Host 'MariaDB root password (leave empty if you did not set one)' -AsSecureString
        $rootPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($rootPw))
    }
    $dbPassword = New-Password
    $sql = "CREATE USER IF NOT EXISTS '$dbUser'@'localhost' IDENTIFIED BY '$dbPassword'; " +
           "CREATE USER IF NOT EXISTS '$dbUser'@'127.0.0.1' IDENTIFIED BY '$dbPassword'; " +
           "ALTER USER '$dbUser'@'localhost' IDENTIFIED BY '$dbPassword'; ALTER USER '$dbUser'@'127.0.0.1' IDENTIFIED BY '$dbPassword'; " +
           "GRANT ALL PRIVILEGES ON ``lmemu\_%``.* TO '$dbUser'@'localhost'; GRANT ALL PRIVILEGES ON ``lmemu\_%``.* TO '$dbUser'@'127.0.0.1'; FLUSH PRIVILEGES;"
    $sqlArgs = @('-u', 'root', '-h', '127.0.0.1', '-e', $sql)
    if ($rootPlain) { $sqlArgs = @('-u', 'root', "-p$rootPlain", '-h', '127.0.0.1', '-e', $sql) }
    & $mariadb @sqlArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the database user (wrong root password?).' }
    Say 'Database user ready.' 'Green'

    # 4. Build.
    Say 'Building the servers (first time takes a few minutes)...'
    & dotnet build (Join-Path $root 'LandmarkEmulator.sln') -c Release -v q -nologo '-clp:ErrorsOnly' | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Build failed (see the messages above).' }

    # 5. gameData: the game's own data tables and English texts (copied from your install, never changed there).
    New-Item -ItemType Directory -Force $gameData | Out-Null
    Copy-Item (Join-Path $game 'Resources\*.txt') $gameData -Force
    $locale = Join-Path $game 'Locale\en_us_data.dat'
    if (Test-Path $locale) { Copy-Item $locale $gameData -Force } else { Say 'Locale\en_us_data.dat not found: names will be missing.' 'Yellow' }
    Copy-Item (Join-Path $root 'data\*') $gameData -Force
    # Ground heights (trees, creature footing, starting build sites): LandmarkEmu-terrain-heights.zip from the
    # project's GitHub Releases, put next to start.bat (or in data\). Without it those stay off; see README.
    $heights = @((Join-Path $root 'LandmarkEmu-terrain-heights.zip'), (Join-Path $root 'data\LandmarkEmu-terrain-heights.zip')) |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($heights) {
        Say 'Unpacking terrain heights...'
        Expand-Archive $heights -DestinationPath $gameData -Force
        Say "Terrain heights for $((Get-ChildItem $gameData -Filter 'TerrainHeights*.bin').Count) landscapes installed." 'Green'
    } else {
        Say 'No LandmarkEmu-terrain-heights.zip found: trees, ground checks and starting build sites will be off (see README).' 'Yellow'
    }
    Say "gameData ready ($((Get-ChildItem $gameData).Count) files)." 'Green'

    # 6. Server configs (the network part comes from server.ini on every start).
    $conn = { param($db) "server=127.0.0.1;port=3306;user=$dbUser;password=$dbPassword;database=$db" }
    $database = [ordered]@{
        Auth      = [ordered]@{ ConnectionString = (& $conn 'lmemu_auth'); Provider = 'MySql' }
        Character = [ordered]@{ ConnectionString = (& $conn 'lmemu_character'); Provider = 'MySql' }
    }
    $auth = [ordered]@{
        Network = [ordered]@{ Host = '127.0.0.1'; Port = 20042 }; Database = $database
        WebApi = [ordered]@{ Enabled = $true; BaseURI = 'http://127.0.0.1:5000' }
        CreationRules = [ordered]@{ StrictName = $false }; GameDataPath = $gameData; WorldAddress = ''
    }
    $world = [ordered]@{
        Network = [ordered]@{ Host = '127.0.0.1'; Port = 19000 }; Database = $database
        GameDataPath = $gameData; GameDirectory = $game; Continent = 406; FreeBuild = $false
    }
    $auth  | ConvertTo-Json -Depth 5 | Set-Content (Join-Path (Find-Build 'AuthServer') 'AuthServer.json') -Encoding UTF8
    $world | ConvertTo-Json -Depth 5 | Set-Content (Join-Path (Find-Build 'WorldServer') 'WorldServer.json') -Encoding UTF8

    # 7. Account.
    $user, $password = Read-Account $Account $Password

    $settings = [pscustomobject][ordered]@{ GamePath = $game; Account = $user; Password = $password; Created = $false }
    Save-Settings $settings $settingsFile   # the password is never saved; the first login stores a derived key
    Say "Settings saved in launcher-settings.json (local only; delete it or run start.bat -Setup to start over).`n" 'Green'
    return $settings
}

function Start-Server([string]$name, [int]$port) {
    if (Get-Process "LandmarkEmulator.$name" -ErrorAction SilentlyContinue) { Say "$name already running."; return }
    $dir = Find-Build $name
    if (-not $dir) { throw "No build of $name found. Run start.bat -Setup." }
    Say "Starting $name..."
    Start-Process (Join-Path $dir "LandmarkEmulator.$name.exe") -WorkingDirectory $dir
    Wait-Port $port 'UDP'
}

# ---------------------------------------------------------------------------------------------------------------
# Invoke-Setup's last output is the settings (anything else it printed went to the screen).
$settings = if (-not $Setup -and (Test-Path $settingsFile)) { Get-Content $settingsFile -Raw | ConvertFrom-Json }
            else { Invoke-Setup | Select-Object -Last 1 }
if (-not (Test-GameFolder $settings.GamePath)) { throw "The game is no longer at '$($settings.GamePath)'. Run start.bat -Setup." }

$ini = Read-ServerIni
if (-not (Get-Process 'LandmarkEmulator.AuthServer', 'LandmarkEmulator.WorldServer' -ErrorAction SilentlyContinue)) {
    Set-ServerNetwork $ini['ServerAddress']
    Set-ServerAdmins $ini['Admins'] $settings.Account
} elseif ($ini['ServerAddress']) {
    Say 'Servers already running: changes to server.ini apply after you close both server windows and run start.bat again.' 'Yellow'
}
# This PC's own game always logs in locally.
Set-ClientConfig $settings.GamePath '127.0.0.1'

Find-MariaDb | Out-Null
Start-Server 'AuthServer' 20042
Wait-Port 5000 'TCP'
Start-Server 'WorldServer' 19000

Connect-AndPlay $settings $settingsFile '127.0.0.1' 'start.bat -Setup' -NoGame:$NoGame
if ($NoGame) { Say 'Servers are up.' 'Green' }
