# Runs the test-only probe build of LP17 Hotfix (probe\Probe.cs): swaps the DLL in, turns dev mode on, launches,
# waits for "[PROBE] done", kills the game, then restores the DLL and Prefs.xml. Saves nothing to the save folder.
param(
    [Parameter(Mandatory)][ValidateSet('static', 'soak', 'dump', 'gear', 'scan', 'autoresearch', 'hemogentap', 'bs9000gif', 'newgame', 'select')][string]$Mode,
    [string]$Save = '',
    [Parameter(Mandatory)][string]$OutLog,
    [string]$OutTable = '',
    [int]$TimeoutSec = 1500,
    [int]$SettleSec = 10,
    # Mod ids to leave out of this one launch, exactly as ModsConfig.xml writes them.
    [string[]]$WithoutMods = @(),
    # Turns FPS+ Error Helper's log filters off for this one launch, so what it hides reaches the log.
    [switch]$Unhide
)
$gameDir = 'F:\Steam\steamapps\common\RimWorld'
$userDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$log = Join-Path $userDir 'Logs\Player.log'
$prefs = Join-Path $userDir 'Config\Prefs.xml'
$live = Join-Path $gameDir 'Mods\16-compat__local.lp17.hotfix\1.7\Assemblies\LP17Hotfix.dll'
$build = Join-Path $PSScriptRoot '..\hotfix\bin\ProbeBuild\LP17Hotfix.dll'
if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) { 'RimWorld already running; not starting'; exit 3 }
# With no workshop items (Steam on another account) Harmony is missing, the load fails and the game resets ModsConfig.xml.
$subs = Select-String -Path 'F:\Steam\logs\workshop_log.txt' -Pattern '\[AppID 294100\].*Number of items: (\d+)' | Select-Object -Last 1
if ($subs -and [int]$subs.Matches[0].Groups[1].Value -eq 0) { 'Steam account has no RimWorld subscriptions; not starting'; exit 4 }
$modsConfig = Join-Path $userDir 'Config\ModsConfig.xml'
$modsConfigText = [IO.File]::ReadAllBytes($modsConfig)
$prefsText = [IO.File]::ReadAllBytes($prefs)
$liveDll = [IO.File]::ReadAllBytes($live)
$fixes = Join-Path $userDir 'Config\Mod_3756967401_FixesMod.xml'
$fixesText = if (Test-Path $fixes) { [IO.File]::ReadAllBytes($fixes) } else { $null }
$verdict = 'timeout'
try {
    [IO.File]::WriteAllText($prefs, ([Text.Encoding]::UTF8.GetString($prefsText) -replace '<devMode>False</devMode>', '<devMode>True</devMode>'))
    if ($WithoutMods) {
        $config = [Text.Encoding]::UTF8.GetString($modsConfigText)
        foreach ($id in $WithoutMods) { $config = $config -replace "\s*<li>$([regex]::Escape($id))</li>", '' }
        [IO.File]::WriteAllText($modsConfig, $config)
    }
    if ($Unhide) {
        [IO.File]::WriteAllText($fixes, '<?xml version="1.0" encoding="utf-8"?><SettingsBlock><ModSettings Class="RimThreadedTTR.Fixes.FixesSettings"><stopSpam>False</stopSpam><hideHarmless>False</hideHarmless><quietLog>False</quietLog><nullTextureGuard>False</nullTextureGuard><meleeGuard>False</meleeGuard></ModSettings></SettingsBlock>')
    }
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
    [IO.File]::WriteAllBytes($modsConfig, $modsConfigText)
    [IO.File]::WriteAllBytes($live, $liveDll)
    if ($null -ne $fixesText) { [IO.File]::WriteAllBytes($fixes, $fixesText) } elseif (Test-Path $fixes) { Remove-Item $fixes }
}
"VERDICT $verdict after $([int]((Get-Date) - $start).TotalSeconds)s"
