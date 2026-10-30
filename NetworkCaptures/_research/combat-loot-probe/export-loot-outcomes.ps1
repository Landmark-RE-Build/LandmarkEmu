# Export S_LootPinataBase SpawnLoot (sub 1) outcomes from Courage (5Iewo…) bins.
# Research only; classifies chunk|bag|essence|other and nearest AddLightweightNpc.
# See loot-outcomes.md / PROMPT-loot-table-tuning.md.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dir = Join-Path $root '5IewoKPQE2BqKg6Q_session\5IewoKPQE2BqKg6Q_session\bins'
$outTsv = Join-Path $root 'loot-outcomes.tsv'
$outMd = Join-Path $root 'loot-outcomes.md'

function Read-U32([byte[]]$b, [ref]$o) { $v = [BitConverter]::ToUInt32($b, $o.Value); $o.Value += 4; $v }
function Read-U64([byte[]]$b, [ref]$o) { $v = [BitConverter]::ToUInt64($b, $o.Value); $o.Value += 8; $v }
function Read-F32([byte[]]$b, [ref]$o) { $v = [BitConverter]::ToSingle($b, $o.Value); $o.Value += 4; $v }
function Read-U8([byte[]]$b, [ref]$o) { $v = $b[$o.Value]; $o.Value += 1; $v }
function Read-2Bit([byte[]]$b, [ref]$o) {
    $first = [uint32]$b[$o.Value]; $o.Value += 1
    $len = $first -band 3; $value = $first
    for ($i = 0; $i -lt $len; $i++) {
        $value = $value -bor ([uint32]$b[$o.Value] -shl (($i + 1) * 8))
        $o.Value += 1
    }
    return ($value -shr 2)
}
function Read-Str([byte[]]$b, [ref]$o) {
    if ($o.Value + 4 -gt $b.Length) { throw "string length past EOF at $($o.Value)" }
    $len = [BitConverter]::ToUInt32($b, $o.Value); $o.Value += 4
    if ($len -eq 0) { return '' }
    if ($o.Value + $len -gt $b.Length) { throw "string body past EOF at $($o.Value) len=$len" }
    $s = [Text.Encoding]::UTF8.GetString($b, $o.Value, [int]$len)
    $o.Value += [int]$len
    $s
}

$npcs = @{}
foreach ($f in Get-ChildItem $dir -Filter '*AddLightweightNpc*') {
    $b = [IO.File]::ReadAllBytes($f.FullName)
    if ($b.Length -lt 20) { continue }
    try {
        $o = 6
        $guid = Read-U64 $b ([ref]$o)
        $null = Read-2Bit $b ([ref]$o)
        $null = Read-Str $b ([ref]$o)
        $name = Read-Str $b ([ref]$o)
        $null = Read-U32 $b ([ref]$o)
        $null = Read-U32 $b ([ref]$o)
        $null = Read-U8 $b ([ref]$o)
        $model = Read-U32 $b ([ref]$o)
        $px = Read-F32 $b ([ref]$o); $py = Read-F32 $b ([ref]$o); $pz = Read-F32 $b ([ref]$o); $null = Read-F32 $b ([ref]$o)
        $null = Read-Str $b ([ref]$o)
        $spawnTint = Read-Str $b ([ref]$o)
        $npcs[$guid] = [pscustomobject]@{ Guid = $guid; ModelId = $model; Name = $name; SpawnTint = $spawnTint; X = $px; Y = $py; Z = $pz }
    }
    catch { }
}

$rows = [System.Collections.Generic.List[object]]::new()
$parseFail = 0
foreach ($f in Get-ChildItem $dir -Filter '*S_LootPinataBase*') {
    $b = [IO.File]::ReadAllBytes($f.FullName)
    if ($b.Length -lt 8 -or $b[0] -ne 0xFB) { continue }
    $o = 2
    try {
        $sub = Read-U8 $b ([ref]$o)
        if ($sub -ne 1) { continue }
        $null = Read-U32 $b ([ref]$o)
        $def = Read-U32 $b ([ref]$o)
        $px = Read-F32 $b ([ref]$o); $py = Read-F32 $b ([ref]$o); $pz = Read-F32 $b ([ref]$o)
        $item = Read-U32 $b ([ref]$o)
        $null = Read-U32 $b ([ref]$o)
        $count = Read-U32 $b ([ref]$o)
        $null = Read-F32 $b ([ref]$o)
        $owner = Read-U64 $b ([ref]$o)
        $null = Read-Str $b ([ref]$o)
        $null = Read-U32 $b ([ref]$o)
        $best = $null; $bestD = [double]::MaxValue
        foreach ($n in $npcs.Values) {
            $dx = $n.X - $px; $dy = $n.Y - $py; $dz = $n.Z - $pz
            $d = [Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
            if ($d -lt $bestD) { $bestD = $d; $best = $n }
        }
        $class = if ($def -eq 5) { 'bag' }
            elseif ($item -in 3682, 3683, 1587, 1588, 1589, 1590, 1591, 1592, 1593) { 'essence' }
            elseif (($item -ge 34 -and $item -le 59) -or ($def -ge 10 -and $def -le 75)) { 'chunk' }
            else { 'other' }
        $seq = ($f.Name -split '_')[0]
        $rows.Add([pscustomobject]@{
                Seq = $seq; File = $f.Name; Def = $def; Item = $item; Count = $count
                Owner = ('{0:X16}' -f $owner)
                X = [Math]::Round($px, 2); Y = [Math]::Round($py, 2); Z = [Math]::Round($pz, 2)
                Class = $class
                NearGuid = if ($best) { '{0:X16}' -f $best.Guid } else { '' }
                NearModel = if ($best) { $best.ModelId } else { '' }
                NearName = if ($best) { $best.Name } else { '' }
                NearTint = if ($best) { $best.SpawnTint } else { '' }
                NearDist = if ($best) { [Math]::Round($bestD, 2) } else { '' }
            })
    }
    catch { $parseFail++ }
}

$tsv = @("seq`tdef`titem`tcount`towner`tx`ty`tz`tclass`tnear_guid`tnear_model`tnear_name`tnear_tint`tnear_dist_m`tfile")
foreach ($r in ($rows | Sort-Object Seq)) {
    $tsv += "$($r.Seq)`t$($r.Def)`t$($r.Item)`t$($r.Count)`t$($r.Owner)`t$($r.X)`t$($r.Y)`t$($r.Z)`t$($r.Class)`t$($r.NearGuid)`t$($r.NearModel)`t$($r.NearName)`t$($r.NearTint)`t$($r.NearDist)`t$($r.File)"
}
[IO.File]::WriteAllLines($outTsv, $tsv)

$byClass = $rows | Group-Object Class | Sort-Object Count -Descending
$bags = $rows | Where-Object Class -eq 'bag'
$bagItems = $bags | Group-Object Item | Sort-Object Count -Descending
$md = @(
    '# Courage loot outcomes (5IewoKPQE2BqKg6Q)',
    '',
    "Generated: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'))Z",
    'Source: S_LootPinataBase sub 1 SpawnLoot. Bag def 5 shows one preview item, not full bag contents.',
    "Parsed rows: $($rows.Count); parse failures: $parseFail; NPCs indexed: $($npcs.Count).",
    '',
    '## By class',
    '',
    '| Class | Count |',
    '|-------|------:|'
)
foreach ($g in $byClass) { $md += "| $($g.Name) | $($g.Count) |" }
$md += '', '## Bag preview items (def 5)', '', '| Item | Bags |', '|------|-----:|'
foreach ($g in $bagItems) { $md += "| $($g.Name) | $($g.Count) |" }
$md += @(
    '',
    '## Diff vs emu (soft)',
    '',
    '- **Confirm:** Lumens (1579) dominate bag previews; Cozy Campfire (3894) and a recipe appear rarely; essences near NPCs.',
    '- **Nudge:** default humanoid / Highfall kill tables — essence-forward content JSON on this branch.',
    '- **Leave:** mining chunks; exact % from n≈10; story no-loot; quiver/weapons.',
    '',
    'See also `forum-loot.md`, `PROMPT-loot-table-tuning.md`.'
)
[IO.File]::WriteAllLines($outMd, $md)
Write-Host "Wrote $outTsv ($($rows.Count) rows) and $outMd"
