# Draws the application icon (a page whose text lines are partly blacked out) and writes
# src/Kuronuri.App/kuronuri.ico with PNG-compressed images at the sizes Windows asks for.
# Usage (repository root):  pwsh tools/make-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "src/Kuronuri.App/kuronuri.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function RoundRect([System.Drawing.Drawing2D.GraphicsPath]$p, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure()
}

function Draw([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $n / 256.0
    $g.ScaleTransform($s, $s)
    $small = $n -le 24

    # Page with a folded corner.
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddLines([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(40, 12), [System.Drawing.PointF]::new(170, 12), [System.Drawing.PointF]::new(216, 58),
        [System.Drawing.PointF]::new(216, 244), [System.Drawing.PointF]::new(40, 244)))
    $page.CloseFigure()
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 70, 76, 86)), ($(if ($small) { 16 } else { 8 }))
    $edge.LineJoin = "Round"
    $g.FillPath([System.Drawing.Brushes]::White, $page)
    $g.DrawPath($edge, $page)
    $fold = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fold.AddLines([System.Drawing.PointF[]]@([System.Drawing.PointF]::new(170, 12), [System.Drawing.PointF]::new(170, 58), [System.Drawing.PointF]::new(216, 58)))
    $g.DrawPath($edge, $fold)

    # Text lines (grey) and redactions (black). Small sizes keep only the bars so they stay readable.
    $grey = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 160, 166, 176))
    $black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 17, 17, 17))
    if (-not $small) {
        foreach ($y in 92, 184) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; RoundRect $p 68 $y 120 14 7; $g.FillPath($grey, $p) }
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath; RoundRect $p 68 214 80 14 7; $g.FillPath($grey, $p)
    }
    $bars = if ($small) { @(@(64, 78, 128, 44), @(64, 150, 128, 44)) } else { @(@(64, 118, 128, 32), @(64, 156, 96, 22)) }
    foreach ($b in $bars) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; RoundRect $p $b[0] $b[1] $b[2] $b[3] 4; $g.FillPath($black, $p) }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($n in $sizes) { , (Draw $n) }
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($n % 256)); $w.Write([byte]($n % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
Write-Output "$out ($((Get-Item $out).Length) bytes)"
