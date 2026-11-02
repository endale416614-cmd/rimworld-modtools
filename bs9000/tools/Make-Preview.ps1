# Draws the Bloodsucker 9000 artwork next to About.xml:
#   About\Preview.png   640x360, Workshop preview
#   About\ModIcon.png   64x64, transparent, mod list icon
# Original artwork, System.Drawing only, installed system fonts only.
# Run (Windows PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Make-Preview.ps1

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$aboutDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'About'
if (-not (Test-Path -LiteralPath $aboutDir)) { throw 'About folder not found next to tools.' }

$DropRadiusRatio = 0.35    # radius of the round bottom, as a fraction of the drop height

# ---------------------------------------------------------------- helpers

function Col([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Pt([double]$x, [double]$y) { [System.Drawing.PointF]::new([single]$x, [single]$y) }
function Rc([double]$x, [double]$y, [double]$w, [double]$h) { [System.Drawing.RectangleF]::new([single]$x, [single]$y, [single]$w, [single]$h) }

function Find-Family([string[]]$names) {
    $installed = @((New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name })
    foreach ($n in $names) {
        if ($installed -contains $n) { return [System.Drawing.FontFamily]::new($n) }
    }
    [System.Drawing.FontFamily]::GenericSansSerif
}

function Set-Quality([System.Drawing.Graphics]$g) {
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
}

# A soft-edged blob: given color in the middle, fading to nothing at the rim of an ellipse.
function Fill-SoftEllipse([System.Drawing.Graphics]$g, [double]$x, [double]$y, [double]$w, [double]$h, [System.Drawing.Color]$center) {
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddEllipse([single]$x, [single]$y, [single]$w, [single]$h)
    $pb = [System.Drawing.Drawing2D.PathGradientBrush]::new($gp)
    $pb.CenterColor = $center
    $pb.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, $center.R, $center.G, $center.B))
    $g.FillPath($pb, $gp)
    $pb.Dispose(); $gp.Dispose()
}

# ---------------------------------------------------------------- the drop

# Teardrop: a point at the top flowing in two S-curves into a round bottom.
function New-DropPath([double]$cx, [double]$top, [double]$h) {
    $r   = $DropRadiusRatio * $h
    $cy  = $top + $h - $r
    $deg = 22.0
    $th  = $deg * [math]::PI / 180.0
    $jx  = $r * [math]::Cos($th)          # where the curve meets the circle
    $jy  = $cy - $r * [math]::Sin($th)
    $k   = 0.22 * $h
    $c2x = $jx - $k * [math]::Sin($th)    # control point along the circle tangent
    $c2y = $jy - $k * [math]::Cos($th)
    $c1x = 0.05 * $h                      # control point just under the tip
    $c1y = $top + 0.30 * $h
    $tip = Pt $cx $top
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier($tip, (Pt ($cx + $c1x) $c1y), (Pt ($cx + $c2x) $c2y), (Pt ($cx + $jx) $jy))
    $p.AddArc([single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r), [single](-$deg), [single](180 + 2 * $deg))
    $p.AddBezier((Pt ($cx - $jx) $jy), (Pt ($cx - $c2x) $c2y), (Pt ($cx - $c1x) $c1y), $tip)
    $p.CloseFigure()
    $p
}

# Glossy red drop with its tip at ($cx, $top), $h pixels tall.
function Add-GlossyDrop([System.Drawing.Graphics]$g, [double]$cx, [double]$top, [double]$h, [bool]$halo) {
    $u    = $h / 168.0                    # pixel sizes below are tuned for a 168 px drop
    $r    = $DropRadiusRatio * $h
    $cy   = $top + $h - $r
    $path = New-DropPath $cx $top $h

    if ($halo) {                          # faint red bloom around the drop, many thin steps
        for ($i = 0; $i -lt 16; $i++) {
            $pen = New-Object System.Drawing.Pen((Col 5 190 14 26), [single]((46 - 2.6 * $i) * $u))
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $path)
            $pen.Dispose()
        }
    }

    # body: light red upper left, deep red lower right (gradient line runs past both ends of the shape)
    $lg = [System.Drawing.Drawing2D.LinearGradientBrush]::new((Pt ($cx - 0.9 * $r) ($top - 3 * $u)), (Pt ($cx + 0.9 * $r) ($top + $h + 3 * $u)), (Col 255 226 36 46), (Col 255 104 3 14))
    $g.FillPath($lg, $path)
    $lg.Dispose()

    $state = $g.Save()
    $g.SetClip($path)

    # bounce light inside the lower right rim
    Fill-SoftEllipse $g ($cx + 0.05 * $r) ($cy + 0.10 * $r) (1.45 * $r) (1.10 * $r) (Col 55 255 70 70)

    # darker rim, drawn as many thin strokes that only show inside the clip
    for ($i = 0; $i -lt 14; $i++) {
        $pen = New-Object System.Drawing.Pen((Col 15 40 2 8), [single]((46 - 3.2 * $i) * $u))
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }

    # keep the narrow tip red instead of letting the rim shade swallow it
    Fill-SoftEllipse $g ($cx - 0.09 * $h) ($top + 0.05 * $h) (0.18 * $h) (0.40 * $h) (Col 150 236 52 60)

    # warm light inside the bulb, upper left
    Fill-SoftEllipse $g ($cx - 1.0 * $r) ($cy - 1.05 * $r) (1.35 * $r) (1.45 * $r) (Col 64 255 90 84)

    # the highlight: small, soft, white, following the curve of the bulb
    $g.TranslateTransform([single]($cx - 0.46 * $r), [single]($cy - 0.46 * $r))
    $g.RotateTransform(36)
    Fill-SoftEllipse $g (-0.15 * $r) (-0.40 * $r) (0.30 * $r) (0.80 * $r) (Col 235 255 255 255)
    $g.ResetTransform()
    Fill-SoftEllipse $g ($cx - 0.10 * $r) ($cy - 0.88 * $r) (0.16 * $r) (0.16 * $r) (Col 150 255 255 255)

    $g.Restore($state)

    # crisp outline
    $pen = New-Object System.Drawing.Pen((Col 230 66 2 10), [single]([math]::Max(1.0, 1.7 * $u)))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $path)
    $pen.Dispose()
    $path.Dispose()
}

# ---------------------------------------------------------------- text

# Title outline with manual tracking (System.Drawing has no letter spacing).
# Winding fill: variable-font glyphs are built from overlapping contours.
function New-TrackedTextPath([System.Drawing.Graphics]$g, [string]$text, [System.Drawing.FontFamily]$fam, [double]$em, [double]$track) {
    $path  = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
    $font  = [System.Drawing.Font]::new($fam, [single]$em, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $sf    = [System.Drawing.StringFormat]::new([System.Drawing.StringFormat]::GenericTypographic)
    $x = 0.0
    foreach ($ch in $text.ToCharArray()) {
        $s = [string]$ch
        if ($s -eq ' ') { $x += 0.30 * $em + $track; continue }
        $path.AddString($s, $fam, [int][System.Drawing.FontStyle]::Regular, [single]$em, (Pt $x 0), $sf)
        $x += $g.MeasureString($s, $font, 10000, $sf).Width + $track
    }
    $font.Dispose(); $sf.Dispose()
    $path
}

function Move-Path([System.Drawing.Drawing2D.GraphicsPath]$p, [double]$dx, [double]$dy) {
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([single]$dx, [single]$dy)
    $p.Transform($m)
    $m.Dispose()
}

function Get-TextWidth([System.Drawing.Graphics]$g, [string]$s, [System.Drawing.Font]$f) {
    $sf = [System.Drawing.StringFormat]::new([System.Drawing.StringFormat]::GenericTypographic)
    $sf.FormatFlags = $sf.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces
    $w = $g.MeasureString($s, $f, 10000, $sf).Width
    $sf.Dispose()
    $w
}

function New-RoundedRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$rad) {
    $d = 2 * $rad
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    $p
}

# ---------------------------------------------------------------- preview 640x360

$W = 640; $H = 360
$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$bmp.SetResolution(96, 96)
$g = [System.Drawing.Graphics]::FromImage($bmp)
Set-Quality $g
$g.Clear((Col 255 9 4 7))

$dropTop = 24.0; $dropH = 180.0
$dropCx  = $W / 2.0
$glowY   = $dropTop + 0.62 * $dropH

# radial deep-red glow behind the drop
$gp = New-Object System.Drawing.Drawing2D.GraphicsPath
$gp.AddEllipse(($dropCx - 400), ($glowY - 270), 800, 540)
$pg = [System.Drawing.Drawing2D.PathGradientBrush]::new($gp)
$cb = New-Object System.Drawing.Drawing2D.ColorBlend
$cb.Colors    = [System.Drawing.Color[]]@((Col 0 84 6 16), (Col 22 84 6 16), (Col 70 96 8 18), (Col 130 112 10 22), (Col 175 128 12 26))
$cb.Positions = [single[]]@(0.0, 0.35, 0.65, 0.88, 1.0)
$pg.InterpolationColors = $cb
$g.FillPath($pg, $gp)
$pg.Dispose(); $gp.Dispose()

Add-GlossyDrop $g $dropCx $dropTop $dropH $true

# title
$titleFam  = Find-Family @('Bahnschrift SemiBold', 'Arial Black', 'Arial')
$titlePath = New-TrackedTextPath $g 'BLOODSUCKER 9000' $titleFam 50 5
$b = $titlePath.GetBounds()
Move-Path $titlePath (($W - $b.Width) / 2.0 - $b.X) (224 - $b.Y)
$b = $titlePath.GetBounds()

$shadow = $titlePath.Clone()
Move-Path $shadow 0 3
foreach ($s in @(@(10, 12), @(6, 22), @(3, 34))) {
    $pen = New-Object System.Drawing.Pen((Col $s[1] 0 0 0), [single]$s[0])
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $shadow)
    $pen.Dispose()
}
$sb = New-Object System.Drawing.SolidBrush((Col 190 0 0 0))
$g.FillPath($sb, $shadow)
$sb.Dispose(); $shadow.Dispose()

$pen = New-Object System.Drawing.Pen((Col 255 92 8 22), 3.0)
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPath($pen, $titlePath)
$pen.Dispose()
$tb = [System.Drawing.Drawing2D.LinearGradientBrush]::new($b, (Col 255 255 250 250), (Col 255 238 178 184), [single]90)
$g.FillPath($tb, $titlePath)
$tb.Dispose()

# tagline, second sentence in red
$tagFam   = Find-Family @('Segoe UI', 'Arial')
$tagFont  = [System.Drawing.Font]::new($tagFam, [single]19, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$tagFontI = [System.Drawing.Font]::new($tagFam, [single]19, [System.Drawing.FontStyle]::Italic, [System.Drawing.GraphicsUnit]::Pixel)
$part1 = 'One hemogen pack a day. '
$part2 = 'The host was not consulted.'
$w1 = Get-TextWidth $g $part1 $tagFont
$w2 = Get-TextWidth $g $part2 $tagFontI
$x0 = ($W - ($w1 + $w2)) / 2.0
$ty = 294.0
$sf = [System.Drawing.StringFormat]::new([System.Drawing.StringFormat]::GenericTypographic)
$sf.FormatFlags = $sf.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces
$br1 = New-Object System.Drawing.SolidBrush((Col 255 226 200 204))
$br2 = New-Object System.Drawing.SolidBrush((Col 255 240 86 94))
$g.DrawString($part1, $tagFont, $br1, (Pt $x0 $ty), $sf)
$g.DrawString($part2, $tagFontI, $br2, (Pt ($x0 + $w1) $ty), $sf)
$br1.Dispose(); $br2.Dispose(); $sf.Dispose()

# corner tag, top right
$cFam  = Find-Family @('Segoe UI Semibold', 'Segoe UI', 'Arial')
$cFont = [System.Drawing.Font]::new($cFam, [single]13, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$label = 'Requires Biotech'
$lw = Get-TextWidth $g $label $cFont
$padX = 11.0; $padY = 5.0
$bw = $lw + 2 * $padX; $bh = $cFont.Height + 2 * $padY
$bx = $W - 16 - $bw; $by = 16.0
$rr = New-RoundedRect $bx $by $bw $bh 6
$fb = New-Object System.Drawing.SolidBrush((Col 235 30 8 12))
$g.FillPath($fb, $rr)
$bp = New-Object System.Drawing.Pen((Col 255 150 22 34), 1.2)
$g.DrawPath($bp, $rr)
$sf = [System.Drawing.StringFormat]::new([System.Drawing.StringFormat]::GenericTypographic)
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$cb2 = New-Object System.Drawing.SolidBrush((Col 255 242 218 220))
$g.DrawString($label, $cFont, $cb2, (Rc $bx $by $bw $bh), $sf)
$fb.Dispose(); $bp.Dispose(); $cb2.Dispose(); $sf.Dispose(); $rr.Dispose()

$g.Dispose()
$bmp.Save((Join-Path $aboutDir 'Preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ---------------------------------------------------------------- icon 64x64

$bmp = New-Object System.Drawing.Bitmap(64, 64, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bmp.SetResolution(96, 96)
$g = [System.Drawing.Graphics]::FromImage($bmp)
Set-Quality $g
$g.Clear([System.Drawing.Color]::Transparent)
$iconH = 0.80 * 64
Add-GlossyDrop $g 32 ((64 - $iconH) / 2.0) $iconH $false
$g.Dispose()
$bmp.Save((Join-Path $aboutDir 'ModIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

'Wrote Preview.png and ModIcon.png'
