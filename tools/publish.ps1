# Puts the newest One Craft and RP Essentials JARs into mods/ and updates manifest.json to point at them.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\publish.ps1 [-Note "Patch note line", "Another line"]
# Then commit and push; every launcher picks the new version up the next time someone presses PLAY.
param([string[]]$Note)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docs = Split-Path $root -Parent
$raw = 'https://raw.githubusercontent.com/vLilMike/OneCraftLauncher/main/mods/'
$mods = Join-Path $root 'mods'
$manifestPath = Join-Path $root 'manifest.json'
$m = Get-Content $manifestPath -Raw | ConvertFrom-Json

function Publish([string]$id, [string]$source) {
    $jar = Get-ChildItem $source | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $jar) { Write-Warning "No JAR found for $id at $source"; return $null }
    $entry = $m.mods | Where-Object { $_.id -eq $id }
    # Always copy: a rebuilt JAR can keep the same file name (same version), and must still replace the old one.
    if ($entry.file -ne $jar.Name) { Get-ChildItem $mods -Filter $entry.file -ErrorAction SilentlyContinue | Remove-Item }
    Copy-Item $jar.FullName (Join-Path $mods $jar.Name) -Force
    $entry.file = $jar.Name
    $entry.url = $raw + $jar.Name
    $entry.sha1 = (Get-FileHash (Join-Path $mods $jar.Name) -Algorithm SHA1).Hash.ToLower()
    Write-Host "$id -> $($jar.Name)"
    return $jar.Name
}

$oc = Publish 'onecraft' (Join-Path $docs 'OnePieceMod\releases\onecraft-*.jar')
Publish 'rpessentials' (Join-Path $docs 'RPEssentials\build\libs\rpessentials-*.jar') | Out-Null
if ($oc -match 'onecraft-([\d\.]+)-') { $m.version = $Matches[1] }

if ($Note) {
    $entry = [pscustomobject]@{ title = "One Craft $($m.version)"; date = (Get-Date -Format 'yyyy-MM-dd'); lines = $Note }
    $m.news = @($entry) + @($m.news)
}

($m | ConvertTo-Json -Depth 10) | Set-Content $manifestPath -Encoding UTF8
Write-Host "manifest.json updated. Now commit and push the OneCraftLauncher repo."
