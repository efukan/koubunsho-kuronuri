# Draws the application icon and writes src/Kuronuri.App/kuronuri.ico (PNG-compressed images, 16-256 px).
# Design: a white page on a paper-grey tile; some words in the text lines are blacked out.
# Flat (no gradients or shadows); the shapes are snapped to whole pixels of each size so small sizes stay crisp.
# Usage (repository root):  pwsh tools/make-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "src/Kuronuri.App/kuronuri.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

$Tile = "#E4E1DA"; $Paper = "#FFFFFF"; $Text = "#B4AFA6"; $Ink = "#121212"

# Geometry is given on a 256 grid.
$script:n = 256
function P([double]$v) { [Math]::Round($v * $script:n / 256.0) }
function RR([double]$x1, [double]$y1, [double]$x2, [double]$y2, [double]$r) {
    $l = P $x1; $t = P $y1; $w = (P $x2) - $l; $h = (P $y2) - $t
    $rr = [Math]::Max(0.0, [Math]::Min($r * $script:n / 256.0, [Math]::Min($w, $h) / 2.0))
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($rr -lt 0.5) { $p.AddRectangle([System.Drawing.RectangleF]::new($l, $t, $w, $h)); return , $p }
    $d = 2 * $rr
    $p.AddArc($l, $t, $d, $d, 180, 90); $p.AddArc($l + $w - $d, $t, $d, $d, 270, 90)
    $p.AddArc($l + $w - $d, $t + $h - $d, $d, $d, 0, 90); $p.AddArc($l, $t + $h - $d, $d, $d, 90, 90); $p.CloseFigure()
    return , $p
}
function Fill($g, [string]$hex, $path) { $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))), $path) }

function Draw([int]$size) {
    $script:n = $size
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "Half"; $g.Clear([System.Drawing.Color]::Transparent)
    Fill $g $Tile (RR 8 8 248 248 56)
    Fill $g $Paper (RR 52 20 204 236 16)
    if ($size -le 24) {
        # Small sizes: a text line, a blacked-out line, a short text line.
        Fill $g $Text (RR 76 64 180 92 6)
        Fill $g $Ink (RR 76 120 180 164 4)
        Fill $g $Text (RR 76 188 140 212 6)
    } else {
        Fill $g $Text (RR 76 50 180 64 7)
        Fill $g $Text (RR 76 80 104 94 7);  Fill $g $Ink (RR 112 74 180 100 5)
        Fill $g $Ink (RR 76 112 140 138 5); Fill $g $Text (RR 148 118 180 132 7)
        Fill $g $Text (RR 76 150 180 164 7)
        Fill $g $Text (RR 76 180 116 194 7); Fill $g $Ink (RR 124 174 168 200 5)
        Fill $g $Text (RR 76 210 140 224 7)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (Draw $s) }
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
Write-Output "$out ($((Get-Item $out).Length) bytes)"
