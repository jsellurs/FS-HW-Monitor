# Regenerates src/FSHWMonitor/Assets/fshw.ico (and docs/icon.png) from code.
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$icoPath = Join-Path $root 'src\FSHWMonitor\Assets\fshw.ico'
$pngPath = Join-Path $root 'docs\icon.png'

function Get-Color([string]$hex) {
    [System.Drawing.Color]::FromArgb(255,
        [Convert]::ToInt32($hex.Substring(0, 2), 16),
        [Convert]::ToInt32($hex.Substring(2, 2), 16),
        [Convert]::ToInt32($hex.Substring(4, 2), 16))
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded dark tile.
    $s = [single]$size
    $radius = $s * 0.22
    $d = $radius * 2
    $rect = New-Object System.Drawing.RectangleF 0.5, 0.5, ($s - 1), ($s - 1)
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $tile.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $tile.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $tile.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Get-Color '111A26'), (Get-Color '06090D'), 90
    $g.FillPath($background, $tile)
    $edge = New-Object System.Drawing.Pen (Get-Color '2A3646'), ([single][Math]::Max(1, $s / 128))
    $g.DrawPath($edge, $tile)

    # 270-degree gauge ring in the four component colors.
    $thickness = $s * 0.105
    $ringRadius = $s * 0.33
    $c = $s / 2
    $arcRect = New-Object System.Drawing.RectangleF ($c - $ringRadius), ($c - $ringRadius), ($ringRadius * 2), ($ringRadius * 2)

    $track = New-Object System.Drawing.Pen (Get-Color '1A2330'), $thickness
    $g.DrawArc($track, $arcRect, 135, 270)

    # Gradient spans the whole tile so the stroke's outer edge doesn't wrap around to the other end.
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::Black), ([System.Drawing.Color]::White), 0
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $blend.Colors = [System.Drawing.Color[]]@((Get-Color '38BDF8'), (Get-Color '34D399'), (Get-Color 'A78BFA'), (Get-Color 'E879F9'))
    $blend.Positions = [single[]]@(0, 0.36, 0.68, 1)
    $gradient.InterpolationColors = $blend
    $ring = New-Object System.Drawing.Pen $gradient, $thickness
    $ring.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ring.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($ring, $arcRect, 135, 270)

    if ($size -ge 32) {
        $font = New-Object System.Drawing.Font 'Bahnschrift', ($s * 0.27), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $textRect = New-Object System.Drawing.RectangleF 0, ($s * 0.01), $s, $s
        $g.DrawString('FS', $font, (New-Object System.Drawing.SolidBrush (Get-Color 'E8EEF6')), $textRect, $format)
    }

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Classic 32-bit DIB icon entry (BITMAPINFOHEADER + bottom-up BGRA + empty AND mask).
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $size, $size),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($size * $size * 4 + $maskRow * $size))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $size * 4) }
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 256, 128, 64, 48, 32, 24, 16
$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    if ($size -eq 256) { , (Get-PngBytes $bmp) } else { , (Get-DibBytes $bmp) }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = [byte]($sizes[$i] % 256)
    $w.Write($dim); $w.Write($dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $w.Write($bytes) }
$w.Flush()

New-Item -ItemType Directory -Force (Split-Path $icoPath) | Out-Null
New-Item -ItemType Directory -Force (Split-Path $pngPath) | Out-Null
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
$preview = New-IconBitmap 256
$preview.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Host "Wrote $icoPath"
Write-Host "Wrote $pngPath"
