# Draws the One Craft Launcher icon: a blocky gold anchor on a navy tile.
# Writes assets/icon.ico (16-256 px) and assets/icon128.png (used as the Minecraft Launcher profile icon).
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'

$art = @(
    '................',
    '.......##.......',
    '......#..#......',
    '......#..#......',
    '.......##.......',
    '....########....',
    '.......##.......',
    '.......##.......',
    '.......##.......',
    '.......##.......',
    '.#.....##.....#.',
    '.##....##....##.',
    '..##...##...##..',
    '...####..####...',
    '.....######.....',
    '................'
)

$navy = [System.Drawing.Color]::FromArgb(255, 11, 20, 36)
$panel = [System.Drawing.Color]::FromArgb(255, 23, 39, 66)
$gold = [System.Drawing.Color]::FromArgb(255, 227, 176, 75)
$goldLight = [System.Drawing.Color]::FromArgb(255, 246, 210, 128)
$goldDark = [System.Drawing.Color]::FromArgb(255, 160, 113, 34)

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'None'
    $g.Clear([System.Drawing.Color]::Transparent)
    $cell = $size / 16.0
    # Tile with a gold rim, blocky corners.
    $b = New-Object System.Drawing.SolidBrush $goldDark
    $g.FillRectangle($b, [float]$cell * 0.5, 0, $size - $cell, $size)
    $g.FillRectangle($b, 0, [float]$cell * 0.5, $size, $size - $cell)
    $b = New-Object System.Drawing.SolidBrush $navy
    $g.FillRectangle($b, [float]$cell * 0.5, [float]$cell * 0.5, $size - $cell, $size - $cell)
    $b = New-Object System.Drawing.SolidBrush $panel
    $g.FillRectangle($b, [float]$cell * 0.5, [float]$cell * 0.5, $size - $cell, [float]($size - $cell) / 2)
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            if ($art[$y][$x] -ne '#') { continue }
            $c = if ($y -lt 5) { $goldLight } elseif ($y -gt 11) { $goldDark } else { $gold }
            $b = New-Object System.Drawing.SolidBrush $c
            $g.FillRectangle($b, [float]($x * $cell), [float]($y * $cell), [float]$cell, [float]$cell)
        }
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 128) { $bmp.Save((Join-Path $assets 'icon128.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO file with PNG-compressed entries.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $assets 'icon.ico'), $out.ToArray())
Write-Host "Icon written to $assets"
