# LandmarkEmu launcher: run start.bat.
#
# First run: asks where Landmark (the 0.589 client) is installed and for an account name / password, then sets
# everything up once — builds the servers, creates the database user, fills gameData from the game's own data tables,
# writes the server configs and creates your account — and saves the answers in launcher-settings.json.
# Every run after that: starts MariaDB's servers check, the auth and world servers, logs in and starts the game.
#
#   start.bat               set up (first time) and play
#   start.bat -NoGame       servers only
#   start.bat -Setup        run the first-time setup again (e.g. the game moved)
# Unattended setup: -GamePath <folder> -Account <name> -Password <pw> [-DbRootPassword <pw>] [-KeepClientConfig]
param([switch]$NoGame, [switch]$Setup, [string]$GamePath, [string]$Account, [string]$Password,
      [string]$DbRootPassword, [switch]$KeepClientConfig)
$ErrorActionPreference = 'Stop'
$rootPasswordGiven = $PSBoundParameters.ContainsKey('DbRootPassword')   # an empty one counts

$root         = $PSScriptRoot
$settingsFile = Join-Path $root 'launcher-settings.json'
$gameData     = Join-Path $root 'gameData'
$dbUser       = 'lmemu_local'

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }

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

function Test-GameFolder([string]$path) {
    return $path -and (Test-Path (Join-Path $path 'Landmark64.exe')) -and (Test-Path (Join-Path $path 'Resources'))
}

function New-Password { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ }) }

function Invoke-Setup {
    Say "`n=== LandmarkEmu first-time setup ===`n" 'Cyan'

    # 1. The game.
    $guesses = @('C:\Program Files (x86)\Steam\steamapps\common\Landmark', 'C:\Program Files\Steam\steamapps\common\Landmark') +
        (Get-PSDrive -PSProvider FileSystem | ForEach-Object { Join-Path $_.Root 'SteamLibrary\steamapps\common\Landmark' })
    $found = $guesses | Where-Object { Test-GameFolder $_ } | Select-Object -First 1
    $game = $null
    if ($GamePath) { if (Test-GameFolder $GamePath) { $game = $GamePath } else { throw "No Landmark64.exe / Resources in '$GamePath'." } }
    while (-not $game) {
        $prompt = if ($found) { "Landmark folder [$found]" } else { 'Landmark folder (the one with Landmark64.exe)' }
        $answer = (Read-Host $prompt).Trim('" ')
        $game = if ($answer) { $answer } else { $found }
        if (-not (Test-GameFolder $game)) { Say "  No Landmark64.exe / Resources in '$game'." 'Yellow'; $game = $null }
    }

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
    # Ground heights (trees, creature footing, ground checks): LandmarkEmu-terrain-heights.zip from the project's
    # GitHub Releases, put next to start.bat (or in data\). Without it those stay off; see README.
    $heights = @((Join-Path $root 'LandmarkEmu-terrain-heights.zip'), (Join-Path $root 'data\LandmarkEmu-terrain-heights.zip')) |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($heights) {
        Say 'Unpacking terrain heights...'
        Expand-Archive $heights -DestinationPath $gameData -Force
        Say "Terrain heights for $((Get-ChildItem $gameData -Filter 'TerrainHeights*.bin').Count) landscapes installed." 'Green'
    } else {
        Say 'No LandmarkEmu-terrain-heights.zip found: trees and ground checks will be off (see README).' 'Yellow'
    }
    Say "gameData ready ($((Get-ChildItem $gameData).Count) files)." 'Green'

    # 6. Server configs (listening on this PC only; see README to open it to your network).
    $conn = { param($db) "server=127.0.0.1;port=3306;user=$dbUser;password=$dbPassword;database=$db" }
    $database = [ordered]@{
        Auth      = [ordered]@{ ConnectionString = (& $conn 'lmemu_auth'); Provider = 'MySql' }
        Character = [ordered]@{ ConnectionString = (& $conn 'lmemu_character'); Provider = 'MySql' }
    }
    $auth = [ordered]@{
        Network = [ordered]@{ Host = '127.0.0.1'; Port = 20042 }; Database = $database
        WebApi = [ordered]@{ Enabled = $true; BaseURI = 'http://127.0.0.1:5000' }   # this PC only
        CreationRules = [ordered]@{ StrictName = $false }; GameDataPath = $gameData
    }
    $world = [ordered]@{
        Network = [ordered]@{ Host = '127.0.0.1'; Port = 19000 }; Database = $database
        GameDataPath = $gameData; GameDirectory = $game; Continent = 406; FreeBuild = $false
    }
    $authDir = Find-Build 'AuthServer'; $worldDir = Find-Build 'WorldServer'
    $auth  | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $authDir 'AuthServer.json') -Encoding UTF8
    $world | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $worldDir 'WorldServer.json') -Encoding UTF8

    # 7. The client has to log in here instead of the retired live servers.
    $config = Join-Path $game 'ClientConfig.ini'
    if (-not (Select-String -Path $config -Pattern '^Server=127\.0\.0\.1:20042' -Quiet)) {
        $yes = if ($KeepClientConfig) { 'n' } else { Read-Host "Point the game at this server? Changes Server= in ClientConfig.ini (a copy is kept as ClientConfig.ini.orig) [Y/n]" }
        if ($yes -notmatch '^[nN]') {
            if (-not (Test-Path "$config.orig")) { Copy-Item $config "$config.orig" }
            $lines = Get-Content $config | ForEach-Object { if ($_ -match '^Server=') { 'Server=127.0.0.1:20042' } else { $_ } }
            if (-not ($lines -match '^Server=')) { $lines += 'Server=127.0.0.1:20042' }
            Set-Content $config $lines -Encoding ASCII
            Say 'ClientConfig.ini now points at 127.0.0.1:20042.' 'Green'
        } else { Say 'Left ClientConfig.ini alone: set Server=127.0.0.1:20042 yourself before playing.' 'Yellow' }
    }

    # 8. Account.
    $user = $Account
    while ($user -notmatch '^[A-Za-z0-9_]{3,32}$') { $user = Read-Host 'Choose an account name (3-32 letters / digits)' }
    # The server refuses passwords under 6 characters (WebAPI /create), so ask until it would accept one.
    $password = $Password
    while (-not $password -or $password.Length -lt 6) {
        if ($password) { Say 'The password needs at least 6 characters.' 'Yellow' }
        $pw = Read-Host 'Choose a password (at least 6 characters)' -AsSecureString
        $password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pw))
    }

    $settings = [ordered]@{ GamePath = $game; Account = $user; Password = $password; Created = $false }
    $settings | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8
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

# Returns @{ Status = HTTP status code; Body = response text }.
function Invoke-Api([string]$method, [string]$path, [string]$user, [string]$password) {
    $url = "http://127.0.0.1:5000/$path`?username=$([uri]::EscapeDataString($user))&password=$([uri]::EscapeDataString($password))"
    $out = & curl.exe -s -X $method -w "`n%{http_code}" $url
    $lines = @($out)
    $status = [int]$lines[-1]
    $body = if ($lines.Count -gt 1) { ($lines[0..($lines.Count - 2)] -join "`n") } else { '' }
    return @{ Status = $status; Body = $body }
}

# ---------------------------------------------------------------------------------------------------------------
# Invoke-Setup's last output is the settings (anything else it printed went to the screen).
$settings = if (-not $Setup -and (Test-Path $settingsFile)) { Get-Content $settingsFile -Raw | ConvertFrom-Json }
            else { Invoke-Setup | Select-Object -Last 1 }
if (-not (Test-GameFolder $settings.GamePath)) { throw "The game is no longer at '$($settings.GamePath)'. Run start.bat -Setup." }

Find-MariaDb | Out-Null
Start-Server 'AuthServer' 20042
Wait-Port 5000 'TCP'
Start-Server 'WorldServer' 19000

if (-not $settings.Created) {
    $r = Invoke-Api 'POST' 'create' $settings.Account $settings.Password
    if ($r.Status -eq 200) { Say "Account '$($settings.Account)' created." 'Green' }
    elseif ($r.Status -eq 409) { Say "Account '$($settings.Account)' already exists; logging in with the saved password." 'Yellow' }
    else { throw "Could not create account '$($settings.Account)' (HTTP $($r.Status)): $($r.Body)`nRun start.bat -Setup to choose another name / password." }
    $settings.Created = $true
    $settings | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8
}

if ($NoGame) { Say 'Servers are up.' 'Green'; return }

$response = Invoke-Api 'POST' 'authenticate' $settings.Account $settings.Password
if ($response.Status -eq 403) {
    throw "Login failed: wrong password for account '$($settings.Account)' (or it does not exist). If that name was already taken by an earlier setup with another password, run start.bat -Setup and pick a new account name."
}
if ($response.Status -eq 429) { throw 'Login failed: too many wrong passwords; wait 5 minutes and try again.' }
$session = if ($response.Status -eq 200) { ($response.Body | ConvertFrom-Json).sessionKey }
if (-not $session) { throw "Login failed (HTTP $($response.Status)): $($response.Body)" }
Say "Launching Landmark as $($settings.Account)..." 'Green'
Start-Process (Join-Path $settings.GamePath 'Landmark64.exe') -ArgumentList "SessionId=$session CasSessionId=0 STEAM_ENABLED=0" -WorkingDirectory $settings.GamePath
