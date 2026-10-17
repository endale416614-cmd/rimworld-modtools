@echo off
title LocalPack v1.7 - Installer
rem Console installer. The PowerShell 5.1 code is the text after the PS-BEGIN marker line below.
set "LP_SELF=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$t=Get-Content -LiteralPath $env:LP_SELF; $i=[array]::IndexOf($t,'#'+'#PS-BEGIN'); iex (($t[($i+1)..($t.Count-1)]) -join [char]10)"
exit /b %errorlevel%

##PS-BEGIN
$ErrorActionPreference = 'Stop'
try {
    $here = Split-Path -Parent $env:LP_SELF
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    Write-Host 'LocalPack v1.7 installer' -ForegroundColor Cyan

    # (a) game / RimSort must be closed
    $run = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in 'RimWorld','RimWorldWin64','RimSort' })
    if ($run.Count -gt 0) { throw ('Close these first: ' + (($run | ForEach-Object { $_.ProcessName } | Sort-Object -Unique) -join ', ')) }

    # (b) find RimWorld
    $rw = 'F:\Steam\steamapps\common\RimWorld'
    while (-not (Test-Path -LiteralPath (Join-Path $rw 'RimWorld.exe'))) {
        $rw = (Read-Host 'RimWorld folder (the one containing RimWorld.exe)').Trim('" ')
    }
    Write-Host "RimWorld: $rw"
    $mods = Join-Path $rw 'Mods'
    if (-not (Test-Path -LiteralPath $mods)) { New-Item -ItemType Directory -Path $mods | Out-Null }

    # (c) back up the live ModsConfig.xml
    $cfgDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
    $cfg = Join-Path $cfgDir 'ModsConfig.xml'
    if (Test-Path -LiteralPath $cfg) {
        $bak = Join-Path $cfgDir "ModsConfig.backup-$stamp.xml"
        Copy-Item -LiteralPath $cfg -Destination $bak
        Write-Host "Backed up ModsConfig.xml -> $bak"
    } else { New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null }

    # (d) move the previous pack version (folders like 05-core__some.packageId) out of Mods
    $old = @(Get-ChildItem -LiteralPath $mods -Directory | Where-Object { $_.Name -match '^\d\d' -and $_.Name -like '*__*' })
    if ($old.Count -gt 0) {
        $bk = Join-Path $rw "LocalPack\Backups\$stamp"
        New-Item -ItemType Directory -Path $bk -Force | Out-Null
        foreach ($d in $old) { Move-Item -LiteralPath $d.FullName -Destination $bk }
        Write-Host "Moved $($old.Count) old pack folders -> $bk"
    }

    # (e) copy the new mods
    $new = @(Get-ChildItem -LiteralPath (Join-Path $here 'Mods') -Directory)
    $n = 0
    foreach ($d in $new) {
        $n++
        Write-Progress -Activity 'Copying mods' -Status "$n / $($new.Count) $($d.Name)" -PercentComplete (100 * $n / $new.Count)
        Copy-Item -LiteralPath $d.FullName -Destination $mods -Recurse -Force
    }
    Write-Progress -Activity 'Copying mods' -Completed

    # (f) activate the pack's mod list
    Copy-Item -LiteralPath (Join-Path $here 'ModsConfig.xml') -Destination $cfg -Force

    # (g) summary
    Write-Host ''
    Write-Host "Done. Installed $n mod folders into $mods" -ForegroundColor Green
    Write-Host 'You can now unsubscribe from the Steam Workshop copies of these mods.'
    Write-Host 'If you keep both, the game logs "same packageId multiple times". Harmless: the local copy wins.'
    Write-Host 'Do NOT Save/Sort in RimSort while the pack is installed (it rewrites ModsConfig.xml).'
} catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host 'Press Enter to close'
