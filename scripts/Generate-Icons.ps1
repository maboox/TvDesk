$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$xml = [Text.StringBuilder]::new()
[void]$xml.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
[void]$xml.AppendLine('  <!-- Generated from the licensed SVGs in assets/lucide by scripts/Generate-Icons.ps1. -->')
foreach ($file in Get-ChildItem (Join-Path $root 'assets/lucide') -Filter '*.svg' | Sort-Object Name) {
    [xml]$svg = Get-Content $file.FullName -Raw
    [void]$xml.AppendLine('  <DrawingImage x:Key="Lucide-' + $file.BaseName + '"><DrawingImage.Drawing><DrawingGroup>')
    foreach ($element in $svg.DocumentElement.ChildNodes) {
        $geometry = switch ($element.LocalName) {
            'path' { '<PathGeometry Figures="' + $element.d + '"/>' }
            'circle' { '<EllipseGeometry Center="' + $element.cx + ',' + $element.cy + '" RadiusX="' + $element.r + '" RadiusY="' + $element.r + '"/>' }
            'rect' { '<RectangleGeometry Rect="' + $element.x + ',' + $element.y + ',' + $element.width + ',' + $element.height + '" RadiusX="' + $element.rx + '" RadiusY="' + $element.rx + '"/>' }
            default { throw "Unsupported SVG element $($element.LocalName)" }
        }
        [void]$xml.AppendLine('    <GeometryDrawing><GeometryDrawing.Pen><Pen Brush="#F5D547" Thickness="1.8" StartLineCap="Round" EndLineCap="Round" LineJoin="Round"/></GeometryDrawing.Pen><GeometryDrawing.Geometry>' + $geometry + '</GeometryDrawing.Geometry></GeometryDrawing>')
    }
    [void]$xml.AppendLine('  </DrawingGroup></DrawingImage.Drawing></DrawingImage>')
}
[void]$xml.AppendLine('</ResourceDictionary>')
[IO.File]::WriteAllText((Join-Path $root 'src/UI/Icons.xaml'), $xml.ToString(), [Text.UTF8Encoding]::new($false))
