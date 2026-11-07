# Builds download/OneCraftLauncher.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK needed, and the EXE runs on any Windows 10/11 PC without installing anything.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$assets = Join-Path $root 'assets'
if (-not (Test-Path (Join-Path $assets 'icon.ico'))) { & (Join-Path $root 'tools\make-icon.ps1') }

$out = Join-Path $root 'download\OneCraftLauncher.exe'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$resources = @()
foreach ($f in Get-ChildItem $assets -File | Where-Object { $_.Extension -in '.jpg', '.png', '.ico' -and $_.Name -ne 'logo.png' }) {
    $resources += "/resource:$($f.FullName),$($f.Name)"
}

$args = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
    "/out:$out",
    "/win32icon:$(Join-Path $assets 'icon.ico')",
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    '/reference:Microsoft.VisualBasic.dll',
    "/reference:$(Join-Path $fw 'System.Web.Extensions.dll')"
) + $resources + (Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
