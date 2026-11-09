# Regenerates the app screenshots in docs/images from the demo mode (sample data; nothing is read or changed).
# Build first (dotnet build winup.slnx), then:
#   powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1
# Each window is captured with PrintWindow, so whatever is on top of it does not end up in the picture.
param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\Winup.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\winup-app.exe"
$out = Join-Path $root 'docs\images'
if (-not (Test-Path $exe)) { throw "Build the app first: $exe" }
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Win]::SetProcessDPIAware() | Out-Null

function Shot([string]$Name, [string[]]$AppArgs, [int]$Wait = 4) {
    $p = Start-Process -FilePath $exe -ArgumentList $AppArgs -PassThru
    try {
        $h = [IntPtr]::Zero
        for ($i = 0; $i -lt 60 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 500; $p.Refresh(); $h = $p.MainWindowHandle }
        if ($h -eq [IntPtr]::Zero) { throw "no window for $Name" }
        Start-Sleep -Seconds $Wait
        $r = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$r) | Out-Null
        $v = New-Object Win+RECT; [Win]::DwmGetWindowAttribute($h, 9, [ref]$v, 16) | Out-Null  # visible frame, without the invisible resize border
        $full = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
        $g = [System.Drawing.Graphics]::FromImage($full)
        $hdc = $g.GetHdc(); [Win]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()  # 2 = PW_RENDERFULLCONTENT
        $crop = New-Object System.Drawing.Rectangle ($v.L - $r.L), ($v.T - $r.T), ($v.R - $v.L), ($v.B - $v.T)
        $img = $full.Clone($crop, $full.PixelFormat)
        $file = Join-Path $out "$Name.png"
        $img.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        "{0}  {1}x{2}" -f $file, $img.Width, $img.Height
        $img.Dispose(); $full.Dispose()
    }
    finally { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue -Confirm:$false }
}

Shot 'app-dark'  @('--demo', '--lang', 'en', '--theme', 'dark')
Shot 'app-light' @('--demo', '--lang', 'en', '--theme', 'light')
Shot 'app-uk'    @('--demo', '--lang', 'uk', '--theme', 'dark')
Shot 'confirm'   @('--demo', '--lang', 'en', '--theme', 'dark', '--scene', 'dialog')
Shot 'done'      @('--demo', '--lang', 'en', '--theme', 'dark', '--scene', 'done') 5
