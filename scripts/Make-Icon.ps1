$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $image = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($image)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(2, 2, 22, 22, 180, 90); $shape.AddArc(40, 2, 22, 22, 270, 90)
    $shape.AddArc(40, 40, 22, 22, 0, 90); $shape.AddArc(2, 40, 22, 22, 90, 90); $shape.CloseFigure()
    $yellow = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F5D547'))
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#191C13'), 3)
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.FillPath($yellow, $shape)
    $graphics.DrawRectangle($pen, 15, 23, 34, 24)
    $graphics.DrawLine($pen, 23, 15, 32, 23); $graphics.DrawLine($pen, 41, 15, 32, 23)
    $graphics.DrawLine($pen, 25, 52, 39, 52)
    $stream = [IO.MemoryStream]::new(); $image.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $frames += @{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose(); $pen.Dispose(); $yellow.Dispose(); $shape.Dispose(); $graphics.Dispose(); $image.Dispose()
}
$path = Join-Path (Split-Path $PSScriptRoot) 'assets/icon.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($path))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
