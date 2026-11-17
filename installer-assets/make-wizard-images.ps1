# Regenerates the installer's wizard images from wwwroot\logo.png in the app's black & red theme.
# Run from anywhere:  powershell -File installer-assets\make-wizard-images.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'wwwroot\logo.png'))
$out  = $PSScriptRoot

function Color($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
$bg = Color '#000000'; $surface = Color '#000000'; $red = Color '#e10600'; $redDeep = Color '#8f0400'

function New-Canvas($w, $h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    return @($bmp, $g)
}

# Tall panel on the welcome/finish pages. Drawn at 100% (164x314) and scaled up by factor.
function Make-Panel($scale) {
    $w = [int](164 * $scale); $h = [int](314 * $scale)
    $bmp, $g = New-Canvas $w $h
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Rectangle]::new(0,0,$w,$h)), $surface, $bg, 90
    $g.FillRectangle($grad, 0, 0, $w, $h)

    # Logo across the top third.
    $lw = $w - [int](16 * $scale); $lh = [int]($lw * $logo.Height / $logo.Width)
    $g.DrawImage($logo, [int](8 * $scale), [int](60 * $scale), $lw, $lh)

    # Red speed stripes sweeping across the bottom.
    foreach ($i in 0..2) {
        $y = $h - [int]((70 - $i * 18) * $scale)
        $c = if ($i -eq 0) { $red } elseif ($i -eq 1) { $redDeep } else { Color '#2a2a32' }
        $pts = @(
            [System.Drawing.PointF]::new(0, $y + 22 * $scale),
            [System.Drawing.PointF]::new($w, $y - 14 * $scale),
            [System.Drawing.PointF]::new($w, $y - 14 * $scale + (7 - $i * 2) * $scale),
            [System.Drawing.PointF]::new(0, $y + 22 * $scale + (7 - $i * 2) * $scale))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush $c), $pts)
    }
    # Thin red edge on the right so the panel separates from the page.
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $red), $w - [int](2 * $scale), 0, [int](2 * $scale), $h)
    $bmp.Save((Join-Path $out ("wizard-{0}.bmp" -f $w)), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose()
}

# Small square in the top-right of the inner pages.
function Make-Small($size) {
    $bmp, $g = New-Canvas $size $size
    $g.Clear($bg)
    $lh = [int]($size * $logo.Height / $logo.Width)
    $g.DrawImage($logo, 0, [int](($size - $lh) / 2), $size, $lh)
    $bmp.Save((Join-Path $out ("wizard-small-{0}.bmp" -f $size)), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose()
}

foreach ($s in 1, 1.25, 1.5, 2) { Make-Panel $s }
foreach ($s in 55, 69, 83, 110) { Make-Small $s }
$logo.Dispose()
