# Builds the in-game textures for the Drain Lance and the Hemogen Tank from the
# source artwork: trim to the opaque bounds, pad square, resample to 256x256.
#
# Run (Windows PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Make-Textures.ps1 -SourceDir "<folder holding the artwork>"

[CmdletBinding()]
param(
    [string]$SourceDir = (Join-Path $env:USERPROFILE 'Desktop'),
    [string]$LanceFile = 'drain lance.png',
    [string]$TankFile  = 'hemogen tank.png',
    [int]$Size = 256
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$modDir      = Split-Path -Parent $PSScriptRoot
$itemDir     = Join-Path $modDir 'Textures\Things\Item\BS9000'
$wornTankDir = Join-Path $modDir 'Textures\Things\Pawn\Humanlike\Apparel\HemogenTank'

foreach ($d in @($itemDir, $wornTankDir)) {
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}

# Smallest rectangle containing every pixel above the alpha threshold.
# LockBits, because GetPixel over a 1500x1500 source takes tens of seconds.
function Get-OpaqueBounds([System.Drawing.Bitmap]$bmp, [int]$alphaThreshold = 8) {
    $rect = [System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $bytes  = New-Object byte[] ($stride * $bmp.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    } finally {
        $bmp.UnlockBits($data)
    }

    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $row = $y * $stride
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            # BGRA little-endian, so alpha is the fourth byte of the pixel.
            if ($bytes[$row + ($x * 4) + 3] -ge $alphaThreshold) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt 0) { throw 'Source image is fully transparent.' }
    [System.Drawing.Rectangle]::new($minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1))
}

function Convert-Texture([string]$inPath, [string]$outPath, [int]$edge, [double]$marginRatio = 0.04) {
    if (-not (Test-Path -LiteralPath $inPath)) { throw "Artwork not found: $inPath" }

    $src = [System.Drawing.Bitmap]::new($inPath)
    try {
        $bounds = Get-OpaqueBounds $src

        # Square the trimmed art, then leave a thin margin so bicubic resampling
        # does not clip the outline against the texture edge.
        $content = [int]([math]::Round($edge * (1.0 - (2.0 * $marginRatio))))
        $scale   = [math]::Min($content / $bounds.Width, $content / $bounds.Height)
        $drawW   = [int]([math]::Round($bounds.Width  * $scale))
        $drawH   = [int]([math]::Round($bounds.Height * $scale))
        $drawX   = [int]([math]::Round(($edge - $drawW) / 2.0))
        $drawY   = [int]([math]::Round(($edge - $drawH) / 2.0))

        $dst = [System.Drawing.Bitmap]::new($edge, $edge, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($dst)
            try {
                $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::None
                $g.Clear([System.Drawing.Color]::Transparent)

                # TileFlipXY stops the resampler sampling transparent black past the source edge,
                # which would otherwise leave a dark halo around the outline.
                $attr = [System.Drawing.Imaging.ImageAttributes]::new()
                try {
                    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                    $g.DrawImage($src,
                        [System.Drawing.Rectangle]::new($drawX, $drawY, $drawW, $drawH),
                        $bounds.X, $bounds.Y, $bounds.Width, $bounds.Height,
                        [System.Drawing.GraphicsUnit]::Pixel, $attr)
                } finally { $attr.Dispose() }
            } finally { $g.Dispose() }

            $dst.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $dst.Dispose() }

        Write-Host ("{0}  <- {1}x{2} trimmed from {3}x{4}" -f (Split-Path -Leaf $outPath), $bounds.Width, $bounds.Height, $src.Width, $src.Height)
    } finally { $src.Dispose() }
}

$lanceSrc = Join-Path $SourceDir $LanceFile
$tankSrc  = Join-Path $SourceDir $TankFile

# A worn overlay is loaded as a Graphic_Multi, so every direction must exist or the
# apparel renders as a missing-texture box. Both items read the same from every side,
# so the direction files are copies of one render. This mirrors the vanilla layout:
# PsychicShockLance and JumpPack each ship a base file plus _north, _south and _east,
# and no _west, which the engine mirrors from _east.
function Add-Directions([string]$basePath) {
    $dir  = Split-Path -Parent $basePath
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($basePath)
    foreach ($d in @('north', 'south', 'east')) {
        Copy-Item -LiteralPath $basePath -Destination (Join-Path $dir "${stem}_$d.png") -Force
        Write-Host ("{0}_{1}.png  <- copy of {0}.png" -f $stem, $d)
    }
}

# The lance is worn from its own item texture, as the vanilla lances are.
$lanceBase = Join-Path $itemDir 'DrainLance.png'
Convert-Texture $lanceSrc $lanceBase $Size
Add-Directions $lanceBase

Convert-Texture $tankSrc (Join-Path $itemDir 'HemogenTank.png') $Size

$wornBase = Join-Path $wornTankDir 'HemogenTank.png'
Convert-Texture $tankSrc $wornBase $Size
Add-Directions $wornBase

Write-Host ''
Write-Host "Textures written under $modDir\Textures"
