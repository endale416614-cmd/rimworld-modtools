# Copies the built mod out of the source tree into dist\ and into every installed copy.
# Only mod content is copied: source, build output, promo material and notes stay behind.
#
# Run (Windows PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Deploy.ps1

[CmdletBinding()]
param(
    [string[]]$Targets = @(
        "$env:USERPROFILE\Desktop\Bloodsucker 9000",
        'F:\Steam\steamapps\common\RimWorld\Mods\09-medical__local.hemogen.tap'
    )
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$modDir  = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $modDir 'dist'

$content = @('About', 'Defs', 'Languages', 'Textures')
$builds  = @{ '1.6' = 'bin\Release-1.6\BS9000.dll'; '1.7' = 'bin\Release\BS9000.dll' }

foreach ($b in $builds.GetEnumerator()) {
    $dll = Join-Path $modDir $b.Value
    if (-not (Test-Path -LiteralPath $dll)) { throw "Missing build output: $dll" }
}

# Stage into dist\ first, so dist\ is always exactly what gets installed.
foreach ($name in $content) {
    $src = Join-Path $modDir $name
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing mod content: $src" }
    $dst = Join-Path $distDir $name
    if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
    Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
}

foreach ($b in $builds.GetEnumerator()) {
    $asmDir = Join-Path $distDir "$($b.Key)\Assemblies"
    if (-not (Test-Path -LiteralPath $asmDir)) { New-Item -ItemType Directory -Force -Path $asmDir | Out-Null }
    Copy-Item -LiteralPath (Join-Path $modDir $b.Value) -Destination (Join-Path $asmDir 'BS9000.dll') -Force
}

Write-Host "staged  $distDir"

foreach ($target in $Targets) {
    if (-not (Test-Path -LiteralPath $target)) {
        Write-Warning "target missing, skipped: $target"
        continue
    }

    foreach ($name in $content) {
        $dst = Join-Path $target $name
        if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
        Copy-Item -LiteralPath (Join-Path $distDir $name) -Destination $dst -Recurse -Force
    }

    foreach ($b in $builds.GetEnumerator()) {
        $asmDir = Join-Path $target "$($b.Key)\Assemblies"
        if (-not (Test-Path -LiteralPath $asmDir)) { New-Item -ItemType Directory -Force -Path $asmDir | Out-Null }
        Copy-Item -LiteralPath (Join-Path $distDir "$($b.Key)\Assemblies\BS9000.dll") -Destination (Join-Path $asmDir 'BS9000.dll') -Force
    }

    Write-Host "deployed $target"
}
