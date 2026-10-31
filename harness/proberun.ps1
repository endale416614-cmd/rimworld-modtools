# Runs the test-only probe build of LP17 Hotfix (probe\Probe.cs): swaps the DLL in, turns dev mode on, launches,
# waits for "[PROBE] done", kills the game, then restores the DLL and Prefs.xml. Saves nothing to the save folder.
param(
    [Parameter(Mandatory)][ValidateSet('static', 'soak')][string]$Mode,
    [string]$Save = '',
    [Parameter(Mandatory)][string]$OutLog,
    [string]$OutTable = '',
    [int]$TimeoutSec = 1500,
    [int]$SettleSec = 10
)
$gameDir = 'F:\Steam\steamapps\common\RimWorld'
$userDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$log = Join-Path $userDir 'Logs\Player.log'
$prefs = Join-Path $userDir 'Config\Prefs.xml'
$live = Join-Path $gameDir 'Mods\16-compat__local.lp17.hotfix\1.7\Assemblies\LP17Hotfix.dll'
$build = Join-Path $PSScriptRoot '..\hotfix\bin\ProbeBuild\LP17Hotfix.dll'
if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) { 'RimWorld already running; not starting'; exit 3 }
$prefsText = [IO.File]::ReadAllBytes($prefs)
$liveDll = [IO.File]::ReadAllBytes($live)
$verdict = 'timeout'
try {
    [IO.File]::WriteAllText($prefs, ([Text.Encoding]::UTF8.GetString($prefsText) -replace '<devMode>False</devMode>', '<devMode>True</devMode>'))
    Copy-Item $build $live -Force
    $env:LP17_PROBE = $Mode; $env:LP17_PROBE_SAVE = $Save; $env:LP17_PROBE_OUT = $OutTable
    $start = Get-Date
    $p = Start-Process -FilePath (Join-Path $gameDir 'RimWorld.exe') -WorkingDirectory $gameDir -PassThru
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSec) {
        Start-Sleep -Seconds 5
        if ($p.HasExited) { $verdict = 'exited'; break }
        if ((Get-Item $log).LastWriteTime -gt $start) {
            $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite,Delete')
            $text = (New-Object IO.StreamReader($fs)).ReadToEnd(); $fs.Dispose()
            if ($text -match '\[PROBE\] done') { $verdict = 'done'; break }
        }
    }
    Start-Sleep -Seconds $SettleSec
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    Start-Sleep -Seconds 3
    Copy-Item $log $OutLog -Force
}
finally {
    [IO.File]::WriteAllBytes($prefs, $prefsText)
    [IO.File]::WriteAllBytes($live, $liveDll)
}
"VERDICT $verdict after $([int]((Get-Date) - $start).TotalSeconds)s"
