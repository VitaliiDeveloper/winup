# Rebuilds the app icon from its SVG sources in src/Winup.App/Assets:
#   icon.svg        drawing for 32 px and up
#   icon-small.svg  simplified drawing for 16 and 24 px (bigger bite and hole, fewer crumbs)
# Each size is rasterized from vector by headless Edge/Chrome, then packed into Assets/app.ico
# (PNG entries, Windows Vista and later). Also writes docs/images/icon.png (256 px) for the README.
#   powershell -ExecutionPolicy Bypass -File tools\icon.ps1
param([string]$Browser)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\Winup.App\Assets'
$build  = Join-Path $root 'artifacts\icon'
New-Item -ItemType Directory -Force $build | Out-Null

if (-not $Browser) {
    $Browser = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Browser) { throw 'Edge or Chrome not found. Pass -Browser <path>.' }

$sizes = @(
    @{ Size = 256; Svg = 'icon.svg' }, @{ Size = 128; Svg = 'icon.svg' }, @{ Size = 64; Svg = 'icon.svg' },
    @{ Size = 48;  Svg = 'icon.svg' }, @{ Size = 32;  Svg = 'icon.svg' },
    @{ Size = 24;  Svg = 'icon-small.svg' }, @{ Size = 16; Svg = 'icon-small.svg' }
)

# One sheet with every size side by side. The SVG markup is inlined because headless browsers
# may refuse to load file:// subresources.
$svgText = @{}
foreach ($f in ($sizes | ForEach-Object { $_.Svg } | Select-Object -Unique)) {
    $svgText[$f] = [IO.File]::ReadAllText((Join-Path $assets $f), [Text.Encoding]::UTF8)
}
$gap = 8; $x = 0; $layout = @()
$cells = foreach ($s in $sizes) {
    $n = $s.Size
    $layout += [pscustomobject]@{ Size = $n; X = $x }
    "<div style=""left:${x}px;width:${n}px;height:${n}px"">$($svgText[$s.Svg])</div>"
    $x += $n + $gap
}
$html = @"
<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;background:transparent;overflow:hidden}
div{position:absolute;top:0}svg{display:block}
</style></head><body>
$($cells -join "`n")
</body></html>
"@
$htmlPath  = Join-Path $build 'sheet.html'
$sheetPath = Join-Path $build 'sheet.png'
[IO.File]::WriteAllText($htmlPath, $html, [Text.UTF8Encoding]::new($false))
Remove-Item $sheetPath -ErrorAction SilentlyContinue

$browserArgs = @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
    '--force-device-scale-factor=1', '--default-background-color=00000000',
    "--user-data-dir=""$(Join-Path $build 'browser-profile')""",
    "--window-size=$x,$(256 + $gap)", "--screenshot=""$sheetPath""",
    """file:///$($htmlPath -replace '\\', '/')"""
)
Start-Process -FilePath $Browser -ArgumentList $browserArgs -Wait -WindowStyle Hidden
if (-not (Test-Path $sheetPath)) { throw "The browser did not write $sheetPath" }

$entries = @()
$sheet = [Drawing.Bitmap]::new($sheetPath)
try {
    if ($sheet.GetPixel(0, 0).A -ne 0) { throw 'The screenshot has no transparency (corner pixel is opaque).' }
    foreach ($l in $layout) {
        $bmp  = $sheet.Clone([Drawing.Rectangle]::new($l.X, 0, $l.Size, $l.Size), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $path = Join-Path $build "icon-$($l.Size).png"
        $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        $entries += [pscustomobject]@{ Size = $l.Size; Bytes = [IO.File]::ReadAllBytes($path) }
    }
} finally { $sheet.Dispose() }

$ms = [IO.MemoryStream]::new(); $bw = [IO.BinaryWriter]::new($ms)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }   # 0 means 256 in the ICO directory
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$e.Bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Flush()
$ico = Join-Path $assets 'app.ico'
[IO.File]::WriteAllBytes($ico, $ms.ToArray()); $bw.Dispose()

Copy-Item (Join-Path $build 'icon-256.png') (Join-Path $root 'docs\images\icon.png') -Force
Write-Host "app.ico: $(($entries | ForEach-Object Size) -join ', ') px, $((Get-Item $ico).Length) bytes"
