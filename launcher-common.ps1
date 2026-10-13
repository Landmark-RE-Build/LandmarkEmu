# Shared by start.ps1 (host: servers + game) and "Play LandmarkEmu.bat" (player: game only; start.bat embeds this file
# in it with player-template.ps1). Dot-sourced or embedded; not run on its own.

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }

function Test-GameFolder([string]$path) {
    return $path -and (Test-Path (Join-Path $path 'Landmark64.exe')) -and (Test-Path (Join-Path $path 'Resources'))
}

# Asks for the Landmark folder (the one with Landmark64.exe), offering a Steam library it finds.
function Select-GameFolder([string]$given) {
    $guesses = @('C:\Program Files (x86)\Steam\steamapps\common\Landmark', 'C:\Program Files\Steam\steamapps\common\Landmark') +
        (Get-PSDrive -PSProvider FileSystem | ForEach-Object { Join-Path $_.Root 'SteamLibrary\steamapps\common\Landmark' })
    $found = $guesses | Where-Object { Test-GameFolder $_ } | Select-Object -First 1
    if ($given) { if (Test-GameFolder $given) { return $given } else { throw "No Landmark64.exe / Resources in '$given'." } }
    while ($true) {
        $prompt = if ($found) { "Landmark folder [$found]" } else { 'Landmark folder (the one with Landmark64.exe)' }
        $answer = (Read-Host $prompt).Trim('" ')
        $game = if ($answer) { $answer } else { $found }
        if (Test-GameFolder $game) { return $game }
        Say "  No Landmark64.exe / Resources in '$game'." 'Yellow'
    }
}

# Asks for an account name and a password (at least 6 characters; the launcher enforces it, the server never sees it).
function Read-Account([string]$user, [string]$password) {
    while ($user -notmatch '^[A-Za-z0-9_]{3,32}$') { $user = Read-Host 'Account name (3-32 letters / digits)' }
    while (-not $password -or $password.Length -lt 6) {
        if ($password) { Say 'The password needs at least 6 characters.' 'Yellow' }
        $pw = Read-Host 'Password (at least 6 characters)' -AsSecureString
        $password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pw))
    }
    return @($user, $password)
}

# Points the game's ClientConfig.ini at a server: login (Server=) and the in-game store pages (MarketplaceUrl=).
# The first change keeps the original as ClientConfig.ini.orig.
function Set-ClientConfig([string]$game, [string]$address) {
    $config = Join-Path $game 'ClientConfig.ini'
    $want = [ordered]@{ 'Server' = "${address}:20042"; 'MarketplaceUrl' = "http://${address}:19001" }
    $lines = @(Get-Content $config)
    $changed = $false
    foreach ($key in $want.Keys) {
        $line = "$key=$($want[$key])"
        if ($lines -contains $line) { continue }
        if ($lines -match "^$key=") { $lines = $lines | ForEach-Object { if ($_ -match "^$key=") { $line } else { $_ } } }
        else { $lines += $line }
        $changed = $true
    }
    if ($changed) {
        if (-not (Test-Path "$config.orig")) { Copy-Item $config "$config.orig" }
        Set-Content $config $lines -Encoding ASCII
        Say "ClientConfig.ini now points at $address (original kept as ClientConfig.ini.orig)." 'Green'
    }
}

# ---- Account / login service (auth server web API, port 5000) -------------------------------------------------
# Logging in uses SRP-6a (LandmarkEmulator.Shared/Network/Cryptography/Srp.cs is the server side): the password never
# leaves this PC. The settings file keeps the derived key (PBKDF2 of the password; it works only for this server's
# account), not the password.

Add-Type -AssemblyName System.Numerics
$script:SrpN = $null

function ConvertTo-HexBytes([string]$hex) {
    $bytes = New-Object byte[] ($hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
    return ,$bytes
}
function ConvertTo-Hex([byte[]]$bytes) { return -join ($bytes | ForEach-Object { $_.ToString('X2') }) }

# Big-endian unsigned bytes <-> BigInteger (which is little-endian two's complement).
function ConvertTo-Big([byte[]]$bigEndian) {
    $le = New-Object byte[] ($bigEndian.Length + 1)
    for ($i = 0; $i -lt $bigEndian.Length; $i++) { $le[$i] = $bigEndian[$bigEndian.Length - 1 - $i] }
    return New-Object System.Numerics.BigInteger (,$le)
}
function ConvertFrom-Big([System.Numerics.BigInteger]$value) {
    $le = $value.ToByteArray()
    $out = New-Object byte[] 256
    $n = [Math]::Min($le.Length, 256)
    for ($i = 0; $i -lt $n; $i++) { $out[255 - $i] = $le[$i] }
    return ,$out
}
function Get-Sha256([byte[][]]$parts) {
    $all = New-Object System.Collections.Generic.List[byte]
    foreach ($part in $parts) { $all.AddRange($part) }
    $sha = [Security.Cryptography.SHA256]::Create()
    return ,$sha.ComputeHash($all.ToArray())
}
function Get-SrpGroup {
    if (-not $script:SrpN) {
        $hex = 'AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310D' +
               'CD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF74' +
               '7359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D' +
               '5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB6' +
               '94B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73'
        $script:SrpN = ConvertTo-Big (ConvertTo-HexBytes $hex)
        $script:SrpG = New-Object System.Numerics.BigInteger 2
        $script:SrpK = ConvertTo-Big (Get-Sha256 @((ConvertFrom-Big $script:SrpN), (ConvertFrom-Big $script:SrpG)))
    }
}
function Get-Mod([System.Numerics.BigInteger]$value) {
    $r = [System.Numerics.BigInteger]::Remainder($value, $script:SrpN)
    if ($r.Sign -lt 0) { $r = $r + $script:SrpN }
    return $r
}

# x = PBKDF2-HMAC-SHA256(password, salt, 10000 iterations, 32 bytes), as hex.
function Get-SrpKey([string]$password, [string]$saltHex) {
    $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($password, (ConvertTo-HexBytes $saltHex), 10000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    return ConvertTo-Hex $kdf.GetBytes(32)
}

function Invoke-Post([string]$address, [string]$path, [hashtable]$query) {
    $q = ($query.GetEnumerator() | ForEach-Object { "$($_.Key)=$([uri]::EscapeDataString([string]$_.Value))" }) -join '&'
    $out = & curl.exe -s --connect-timeout 10 -X POST -w "`n%{http_code}" "http://${address}:5000/$path`?$q"
    $lines = @($out)
    $status = [int]$lines[-1]
    $body = if ($lines.Count -gt 1) { ($lines[0..($lines.Count - 2)] -join "`n") } else { '' }
    if ($status -eq 0) { throw "No answer from the server at $address (port 5000). Is it running, and are its ports open?" }
    return @{ Status = $status; Body = $body }
}

# Creates the account: a new random salt and the verifier g^x mod N go to the server, not the password.
function Register-Account([string]$address, [string]$user, [string]$password) {
    Get-SrpGroup
    $salt = ConvertTo-Hex ([Security.Cryptography.RandomNumberGenerator]::Create() | ForEach-Object { $b = New-Object byte[] 16; $_.GetBytes($b); $b })
    $key = Get-SrpKey $password $salt
    $v = [System.Numerics.BigInteger]::ModPow($script:SrpG, (ConvertTo-Big (ConvertTo-HexBytes $key)), $script:SrpN)
    $r = Invoke-Post $address 'srp/register' @{ username = $user; salt = $salt; verifier = (ConvertTo-Hex (ConvertFrom-Big $v)) }
    return @{ Status = $r.Status; Body = $r.Body; Salt = $salt; Key = $key }
}

# SRP login. Returns @{ SessionKey; Salt; Key } (Key / Salt to remember instead of the password).
function Invoke-SrpLogin([string]$address, [string]$user, [string]$keyHex, [string]$keySalt, [string]$password) {
    Get-SrpGroup
    $start = Invoke-Post $address 'srp/start' @{ username = $user }
    if ($start.Status -eq 429) { throw 'Login failed: too many wrong passwords; wait 5 minutes and try again.' }
    if ($start.Status -ne 200) { throw "Login failed (HTTP $($start.Status)): $($start.Body)" }
    $hello = $start.Body | ConvertFrom-Json
    if (-not $keyHex -or $keySalt -ne $hello.salt) {
        if (-not $password) { throw 'No saved key for this server; run the setup again (-Setup).' }
        $keyHex = Get-SrpKey $password $hello.salt
    }
    $x = ConvertTo-Big (ConvertTo-HexBytes $keyHex)
    $publicB = ConvertTo-Big (ConvertTo-HexBytes $hello.b)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $aBytes = New-Object byte[] 32; $rng.GetBytes($aBytes)
    # PowerShell names ignore case: the secret is $secretA, the public value $publicA.
    $secretA = ConvertTo-Big $aBytes
    $publicA = [System.Numerics.BigInteger]::ModPow($script:SrpG, $secretA, $script:SrpN)
    $padA = ConvertFrom-Big $publicA; $padB = ConvertFrom-Big $publicB
    $u = ConvertTo-Big (Get-Sha256 @($padA, $padB))
    $gx = [System.Numerics.BigInteger]::ModPow($script:SrpG, $x, $script:SrpN)
    $base = Get-Mod ($publicB - (Get-Mod ($script:SrpK * $gx)))
    $S = [System.Numerics.BigInteger]::ModPow($base, $secretA + $u * $x, $script:SrpN)
    $padS = ConvertFrom-Big $S
    $m1 = Get-Sha256 @($padA, $padB, $padS)
    $finish = Invoke-Post $address 'srp/finish' @{ handshake = $hello.handshake; a = (ConvertTo-Hex $padA); m1 = (ConvertTo-Hex $m1) }
    if ($finish.Status -eq 403) { throw "Login failed: wrong password for account '$user' (or it does not exist)." }
    if ($finish.Status -eq 429) { throw 'Login failed: too many wrong passwords; wait 5 minutes and try again.' }
    if ($finish.Status -ne 200) { throw "Login failed (HTTP $($finish.Status)): $($finish.Body)" }
    $done = $finish.Body | ConvertFrom-Json
    # The server proves it knows the verifier too (M2), then the session key is unmasked with H(S | "session").
    $m2 = ConvertTo-Hex (Get-Sha256 @($padA, $m1, $padS))
    if ($m2 -ne $done.m2) { throw 'Login failed: the server could not prove it is the real server.' }
    $mask = Get-Sha256 @($padS, [Text.Encoding]::ASCII.GetBytes('session'))
    $sealed = ConvertTo-HexBytes $done.sessionKey
    for ($i = 0; $i -lt $sealed.Length; $i++) { $sealed[$i] = $sealed[$i] -bxor $mask[$i] }
    return @{ SessionKey = [Text.Encoding]::ASCII.GetString($sealed); Salt = $hello.salt; Key = $keyHex }
}

# Saves settings without the password.
function Save-Settings($settings, [string]$settingsFile) {
    $settings | Select-Object -Property * -ExcludeProperty Password | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8
}

# Creates the account the first time, logs in (SRP) and starts the game. $redo is the command that starts over
# (start.bat -Setup / "Play LandmarkEmu.bat" -Setup).
function Connect-AndPlay($settings, [string]$settingsFile, [string]$address, [string]$redo, [switch]$NoGame) {
    $password = $settings.Password
    if (-not $settings.Key -and -not $password) { $null, $password = Read-Account $settings.Account '' }
    if (-not $settings.Created) {
        $r = Register-Account $address $settings.Account $password
        if ($r.Status -eq 200) {
            Say "Account '$($settings.Account)' created." 'Green'
            $settings | Add-Member -Force NoteProperty Key $r.Key
            $settings | Add-Member -Force NoteProperty Salt $r.Salt
        }
        elseif ($r.Status -eq 409) { Say "Account '$($settings.Account)' already exists; logging in with your password." 'Yellow' }
        elseif ($r.Status -eq 429) { throw 'Too many new accounts from this address; try again in an hour.' }
        else { throw "Could not create account '$($settings.Account)' (HTTP $($r.Status)): $($r.Body)`nRun $redo to choose another name / password." }
        $settings | Add-Member -Force NoteProperty Created $true
    }
    $login = Invoke-SrpLogin $address $settings.Account $settings.Key $settings.Salt $password
    $settings | Add-Member -Force NoteProperty Key $login.Key
    $settings | Add-Member -Force NoteProperty Salt $login.Salt
    Save-Settings $settings $settingsFile
    if ($NoGame) { return }
    Say "Launching Landmark as $($settings.Account)..." 'Green'
    Start-Process (Join-Path $settings.GamePath 'Landmark64.exe') -ArgumentList "SessionId=$($login.SessionKey) CasSessionId=0 STEAM_ENABLED=0" -WorkingDirectory $settings.GamePath
}
