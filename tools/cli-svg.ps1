# Renders `winup check --demo` (sample data; nothing is read or changed) as docs/images/cli-check.svg.
# Build first (dotnet build winup.slnx), then:
#   powershell -ExecutionPolicy Bypass -File tools\cli-svg.ps1
param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\Winup.Cli\bin\$Configuration\net10.0-windows\winup.exe"
$out = Join-Path $root 'docs\images\cli-check.svg'
if (-not (Test-Path $exe)) { throw "Build the CLI first: $exe" }

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$text = (& $exe check --demo --no-pick --color --lang en) -join "`n"
$command = 'winup check --demo'

$colors = @{ '32' = '#6CCB5F'; '33' = '#F9D25B'; '31' = '#FF6B6B' }
$fg = '#D4D4D4'; $dim = '#8A8A8A'
$lineH = 21; $padX = 22; $top = 64
$lines = @("PS> $command", '') + ($text -split "`n")
$width = 760; $height = $top + $lines.Count * $lineH + 18

function Esc([string]$s) { $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace(' ', [string][char]0x00A0) }  # no-break spaces keep the columns in every renderer

$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine("<svg xmlns=""http://www.w3.org/2000/svg"" width=""$width"" height=""$height"" viewBox=""0 0 $width $height"" role=""img"" aria-label=""winup check: a numbered list of what can be cleaned, with levels and sizes"">")
[void]$sb.AppendLine("<rect width=""$width"" height=""$height"" rx=""10"" fill=""#1E1E1E""/>")
[void]$sb.AppendLine("<rect width=""$width"" height=""40"" rx=""10"" fill=""#2B2B2B""/><rect y=""30"" width=""$width"" height=""10"" fill=""#2B2B2B""/>")
[void]$sb.AppendLine("<text x=""$padX"" y=""25"" font-family=""Segoe UI, sans-serif"" font-size=""13"" fill=""#BBBBBB"">Windows PowerShell</text>")
[void]$sb.AppendLine("<g font-family=""Cascadia Mono, Consolas, monospace"" font-size=""14"" xml:space=""preserve"" style=""white-space:pre"">")

$y = $top
foreach ($line in $lines) {
    $parts = [regex]::Split($line, '(\x1b\[[0-9;]*m)')
    $color = $fg; $bold = $false; $spans = New-Object Text.StringBuilder
    foreach ($part in $parts) {
        if ($part -match '^\x1b\[([0-9;]*)m$') {
            switch ($Matches[1]) {
                '0' { $color = $fg; $bold = $false }
                '1' { $bold = $true }
                '2' { $color = $dim }
                default { if ($colors.ContainsKey($Matches[1])) { $color = $colors[$Matches[1]] } }
            }
            continue
        }
        if ($part.Length -eq 0) { continue }
        $weight = if ($bold) { ' font-weight="bold"' } else { '' }
        [void]$spans.Append("<tspan fill=""$color""$weight>$(Esc $part)</tspan>")
    }
    if ($line.StartsWith('PS> ')) { $spans = "<tspan fill=""#6CA0DC"">PS&gt; </tspan><tspan fill=""$fg"">$(Esc $command)</tspan>" }
    [void]$sb.AppendLine("<text x=""$padX"" y=""$y"">$spans</text>")
    $y += $lineH
}
[void]$sb.AppendLine('</g></svg>')
[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
"$out  ${width}x${height}"
