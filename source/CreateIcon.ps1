$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::White)
    $g.ScaleTransform($size / 1024.0, $size / 1024.0)
    $gray = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(102, 102, 102))
    $g.FillEllipse($gray, 112, 112, 800, 800)
    $triangle = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $triangle.AddBezier(430,326,410,314,394,328,394,351)
    $triangle.AddLine(394,351,394,673)
    $triangle.AddBezier(394,673,394,696,410,710,430,698)
    $triangle.AddLine(430,698,686,531)
    $triangle.AddBezier(686,531,706,519,706,505,686,493)
    $triangle.AddLine(686,493,430,326)
    $triangle.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $triangle)
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$memory.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'AppIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $triangle.Dispose(); $gray.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'AppIcon.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $encodedSize = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
