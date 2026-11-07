# Builds the launcher icon from the One Craft logo (assets/logo.png).
# Writes assets/icon.ico (16-256 px, used for the EXE and taskbar) and assets/icon128.png (the Minecraft Launcher profile icon).
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'
$logo = [System.Drawing.Bitmap]::FromFile((Join-Path $assets 'logo.png'))

# Trim the empty margin so the logo fills the icon.
$minX = $logo.Width; $minY = $logo.Height; $maxX = 0; $maxY = 0
for ($y = 0; $y -lt $logo.Height; $y += 2) {
    for ($x = 0; $x -lt $logo.Width; $x += 2) {
        if ($logo.GetPixel($x, $y).A -gt 16) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$w = $maxX - $minX + 2; $h = $maxY - $minY + 2
$side = [Math]::Max($w, $h)

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $size / $side
    $dw = $w * $scale; $dh = $h * $scale
    $dest = New-Object System.Drawing.RectangleF (($size - $dw) / 2), (($size - $dh) / 2), $dw, $dh
    $g.DrawImage($logo, $dest, (New-Object System.Drawing.RectangleF $minX, $minY, $w, $h), 'Pixel')
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
$logo.Dispose()

# ICO file with PNG-compressed entries.
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $assets 'icon.ico'), $out.ToArray())
Write-Host "Icon written to $assets"
