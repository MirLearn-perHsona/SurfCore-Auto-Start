$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $outDir 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $s / 256.0
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $path = New-RoundedPath 0 0 $s $s ([float](56 * $scale))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 13, 116, 144), [System.Drawing.Color]::FromArgb(255, 6, 182, 212), 45.0)
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()

    $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $penW = [Math]::Max(1.3, 13 * $scale)
    $clockPen = New-Object System.Drawing.Pen($white, $penW)
    $clockPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $clockPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cx = 128 * $scale; $cy = 132 * $scale; $rad = 59 * $scale
    $g.DrawEllipse($clockPen, [float]($cx - $rad), [float]($cy - $rad), [float]($rad * 2), [float]($rad * 2))
    $g.DrawLine($clockPen, [float]$cx, [float]$cy, [float]$cx, [float]($cy - 32 * $scale))
    $g.DrawLine($clockPen, [float]$cx, [float]$cy, [float]($cx + 26 * $scale), [float]($cy + 8 * $scale))
    $clockPen.Dispose()

    $dotBrush = New-Object System.Drawing.SolidBrush($white)
    $g.FillEllipse($dotBrush, [float]($cx - 4.5 * $scale), [float]($cy - 4.5 * $scale), [float](9 * $scale), [float](9 * $scale))
    $dotBrush.Dispose()
    $g.Dispose()

    $pngPath = Join-Path $assets ("icon-{0}.png" -f $s)
    $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += [pscustomobject]@{ Size = $s; Path = $pngPath }
}

# Assemble ICO with PNG-compressed entries.
$entries = @()
$blobs = @()
foreach ($p in $pngs) {
    $bytes = [System.IO.File]::ReadAllBytes($p.Path)
    $blobs += ,$bytes
}
$headerSize = 6 + 16 * $entries.Count
$offset = 6 + 16 * $pngs.Count
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$pngs.Count)
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $s = $pngs[$i].Size
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$blobs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($b in $blobs) { $bw.Write($b) }
$bw.Flush()
$icoPath = Join-Path $outDir 'app.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Output "Wrote $icoPath"
