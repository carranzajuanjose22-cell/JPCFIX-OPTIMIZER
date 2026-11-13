$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../src/JPCFIX.Optimizer.Desktop/Assets'
[void][IO.Directory]::CreateDirectory($assetDirectory)
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform(($size / 100.0), ($size / 100.0))
    $shield = New-Object Drawing.Drawing2D.GraphicsPath
    $shield.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(50,4),[Drawing.PointF]::new(91,19),[Drawing.PointF]::new(87,62),[Drawing.PointF]::new(73,83),[Drawing.PointF]::new(50,97),[Drawing.PointF]::new(27,83),[Drawing.PointF]::new(13,62),[Drawing.PointF]::new(9,19)))
    $fill = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#172B50'))
    $border = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#60A5FA'), 3)
    $ink = New-Object Drawing.Pen([Drawing.Color]::White, 5)
    $ink.StartCap = $ink.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $accent = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#36DFC0'), 5)
    $accent.StartCap = $accent.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.FillPath($fill, $shield); $graphics.DrawPath($border, $shield)
    $jp = New-Object Drawing.Drawing2D.GraphicsPath
    $jp.AddLine(29,30,46,30); $jp.AddLine(46,30,46,56); $jp.AddBezier(46,56,46,71,28,71,28,57)
    $jp.StartFigure(); $jp.AddLine(59,66,59,30); $jp.AddLine(59,30,69,30); $jp.AddBezier(69,30,86,30,86,51,69,51); $jp.AddLine(69,51,59,51)
    $graphics.DrawPath($ink, $jp)
    $graphics.DrawLines($accent, [Drawing.PointF[]]@([Drawing.PointF]::new(44,79),[Drawing.PointF]::new(51,85),[Drawing.PointF]::new(67,70)))
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $frames += @{ Size = $size; Data = $memory.ToArray() }
    $memory.Dispose(); $jp.Dispose(); $shield.Dispose(); $fill.Dispose(); $border.Dispose(); $ink.Dispose(); $accent.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create((Join-Path $assetDirectory 'Jpcfix.ico'))
$writer = New-Object IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output 'Ícono JPCFIX generado (16–256 px).'
