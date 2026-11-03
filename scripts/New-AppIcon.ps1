$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$iconPath = Join-Path $root 'src/WindowsAlarm/Assets/AlarmClock.ico'
$previewPath = Join-Path $root 'artifacts/alarm-icon-preview.png'

# Draw from a 256-unit canvas, then supersample each Windows icon size.
function New-ClockBitmap([int]$Size) {
    $canvas = [System.Drawing.Bitmap]::new($Size * 4, $Size * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $navy = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#173B78'))
    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#2563EB'))
    $orange = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFB13B'))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $feet = [System.Drawing.Pen]::new($navy, 16)
    $hands = [System.Drawing.Pen]::new($navy, 12)
    $ticks = [System.Drawing.Pen]::new($navy, 7)
    foreach ($pen in @($feet, $hands, $ticks)) {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    }
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($Size * 4 / 256.0), ($Size * 4 / 256.0))
        $graphics.DrawLine($feet, 81, 204, 64, 232)
        $graphics.DrawLine($feet, 175, 204, 192, 232)
        $graphics.DrawLine($feet, 128, 31, 128, 54)
        $graphics.FillEllipse($navy, 113, 20, 30, 14)
        foreach ($bell in @(@(65, 48, -36), @(191, 48, 36))) {
            $state = $graphics.Save()
            $graphics.TranslateTransform($bell[0], $bell[1])
            $graphics.RotateTransform($bell[2])
            $graphics.FillEllipse($navy, -38, -25, 76, 50)
            $graphics.FillEllipse($orange, -33, -22, 66, 42)
            $graphics.Restore($state)
        }
        $graphics.FillEllipse($navy, 34, 43, 188, 188)
        $graphics.FillEllipse($blue, 39, 48, 178, 178)
        $graphics.FillEllipse($white, 54, 63, 148, 148)
        $graphics.DrawLine($ticks, 128, 76, 128, 85)
        $graphics.DrawLine($ticks, 128, 189, 128, 198)
        $graphics.DrawLine($ticks, 67, 137, 76, 137)
        $graphics.DrawLine($ticks, 180, 137, 189, 137)
        $graphics.DrawLine($hands, 128, 137, 99, 113)
        $graphics.DrawLine($hands, 128, 137, 161, 104)
        $graphics.FillEllipse($orange, 117, 126, 22, 22)
        $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
        $scaled = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaled.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scaled.DrawImage($canvas, 0, 0, $Size, $Size)
        } finally { $scaled.Dispose() }
        return ,$bitmap
    } finally {
        $graphics.Dispose()
        $canvas.Dispose()
        foreach ($item in @($feet, $hands, $ticks, $navy, $blue, $orange, $white)) { $item.Dispose() }
    }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @(
    foreach ($size in $sizes) {
        $bitmap = New-ClockBitmap $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            if ($size -eq 256) {
                [System.IO.Directory]::CreateDirectory((Split-Path $previewPath -Parent)) | Out-Null
                $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            ,$stream.ToArray()
        } finally { $bitmap.Dispose(); $stream.Dispose() }
    }
)
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = $sizes[$index] % 256
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Write-Output ('Created icon: ' + $iconPath)
