# RimWorld launch-test harness. Windows PowerShell 5.1. Load test only: kills the game at the end.
param([int]$SettleSec = 20)
$ErrorActionPreference = 'Stop'
$step = 'init'
$exitCode = 2
$gameDir  = 'F:\Steam\steamapps\common\RimWorld'
$gameExe  = Join-Path $gameDir 'RimWorld.exe'
$logDir   = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs'
$logFile  = Join-Path $logDir 'Player.log'
$harmLog  = Join-Path $logDir 'harmony.log.txt'
$modsCfg  = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml'
$toolsDir = 'J:\Rimworld Mod\ModlistAudit-2026-10-17\tools'
$gameNames = @('RimWorld', 'RimWorldWin64')
$hardTimeoutSec = 1800
$errRx = '^\S+ \S+\s+ERROR '

function Get-Game { @(Get-Process -Name $gameNames -ErrorAction SilentlyContinue) }

# incremental log reader: scans new complete lines since the last call
$st = @{ off = 0L; fresh = $false; size = 0L; errs = 0; marker = $false; verse = $false }
function Read-NewLog {
    param([datetime]$launchUtc)
    if (-not (Test-Path -LiteralPath $logFile)) { return }
    $fi = Get-Item -LiteralPath $logFile
    if (-not $st.fresh) {
        if ($fi.LastWriteTimeUtc -lt $launchUtc) { return }   # stale log from previous run
        $st.fresh = $true; $st.off = 0L
    }
    if ($fi.Length -lt $st.off) { $st.off = 0L; $st.errs = 0 }
    $st.size = $fi.Length
    if ($fi.Length -eq $st.off) { return }
    $fs = [System.IO.File]::Open($logFile, 'Open', 'Read', [System.IO.FileShare]'ReadWrite,Delete')
    try {
        [void]$fs.Seek($st.off, 'Begin')
        $n = [int]($fs.Length - $st.off)
        $buf = New-Object byte[] $n
        $got = 0
        while ($got -lt $n) { $r = $fs.Read($buf, $got, $n - $got); if ($r -le 0) { break }; $got += $r }
    } finally { $fs.Dispose() }
    if ($got -le 0) { return }
    $last = [Array]::LastIndexOf($buf, [byte]10, $got - 1)
    if ($last -lt 0) { return }
    $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $last + 1)
    $st.off += $last + 1
    foreach ($l in ($text -split "`n")) {
        if ($l -match $errRx) { $st.errs++ }
        if ($l.Contains('[GUIPerf]') -and ($l -match 'RimWorld 1\.7\.\d+ rev\d+')) { $st.marker = $true }
        if ($l -match '\[Verse\] RimWorld 1\.7\.\d+ rev\d+') { $st.verse = $true }
    }
}

$runDir = $null
try {
    # 1
    $step = '1 preflight'
    if (@(Get-Process -Name 'RimSort','rimstack' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'RimSort or RimStack is running; refusing (both rewrite ModsConfig.xml).' }
    if ((Get-Game).Count -gt 0) { throw 'RimWorld is already running; refusing.' }
    if (-not (Test-Path -LiteralPath $gameExe)) { throw "Missing $gameExe" }

    # 2
    $step = '2 runs folder'
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $runDir = Join-Path $toolsDir "runs\$stamp"
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null

    # 3
    $step = '3 copy ModsConfig'
    Copy-Item -LiteralPath $modsCfg -Destination (Join-Path $runDir 'ModsConfig.xml')

    # 4
    $step = '4 start game'
    $launchUtc = (Get-Date).ToUniversalTime().AddSeconds(-1)
    Start-Process -FilePath $gameExe -WorkingDirectory $gameDir
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds 15
    if ((Get-Game).Count -eq 0) { throw 'No RimWorld/RimWorldWin64 process found 15 s after launch.' }
    Write-Host "Game process up (pid $((Get-Game)[0].Id))."

    # 5
    $step = '5 poll'
    $stopReason = $null
    $lastSize = -1L
    $lastChange = $sw.Elapsed.TotalSeconds
    $nextProgress = 0
    while ($true) {
        Start-Sleep -Seconds 10
        $el = $sw.Elapsed.TotalSeconds
        Read-NewLog -launchUtc $launchUtc
        if ($st.size -ne $lastSize) { $lastSize = $st.size; $lastChange = $el }
        if ($el -ge $nextProgress) {
            Write-Host ("[{0,5}s] log {1,10} bytes  errors {2}" -f [int]$el, $st.size, $st.errs)
            $nextProgress = $el + 60
        }
        if ($st.marker) { $stopReason = 'marker'; break }
        if ((Get-Game).Count -eq 0) { $stopReason = 'process_exited'; break }
        if ($st.fresh -and $st.verse -and (($el - $lastChange) -ge 300)) { $stopReason = 'fallback_idle_300s'; break }
        if ($el -ge $hardTimeoutSec) { $stopReason = 'timeout_30min'; break }
    }
    $elapsedAtStop = [int]$sw.Elapsed.TotalSeconds
    Write-Host "Stop reason: $stopReason at ${elapsedAtStop}s"

    # 6
    $step = '6 settle and stop'
    if ($stopReason -ne 'process_exited') { Start-Sleep -Seconds $SettleSec }
    $procs = Get-Game
    foreach ($p in $procs) { try { Stop-Process -Id $p.Id -Force } catch {} }
    foreach ($p in $procs) { try { [void]$p.WaitForExit(30000) } catch {} }
    $totalSec = [int]$sw.Elapsed.TotalSeconds

    # 7
    $step = '7 copy logs'
    $runLog = Join-Path $runDir 'Player.log'
    Copy-Item -LiteralPath $logFile -Destination $runLog
    if (Test-Path -LiteralPath $harmLog) { Copy-Item -LiteralPath $harmLog -Destination (Join-Path $runDir 'harmony.log.txt') }

    # 8
    $step = '8 parse'
    $lines = [System.IO.File]::ReadAllLines($runLog, [System.Text.Encoding]::UTF8)
    $hdr = '^\S+ \S+\s+(ERROR|WARN|INFO) '
    $cnt = @{ ERROR = 0; WARN = 0; INFO = 0 }
    $subs = @('HarmonyInfix','DivideByZero','ImageOpt','ReflectionTypeLoadException','Error while instantiating a mod','Error in static constructor','same packageId multiple times','Could not load Texture2D','MatFrom with null sourceTex')
    $hits = [ordered]@{}; foreach ($s in $subs) { $hits[$s] = 0 }
    $harmLine = $null; $startedLine = $null; $logClosed = $false
    $freq = @{}
    foreach ($l in $lines) {
        if ($l.Length -eq 0) { continue }
        $m = [regex]::Match($l, $hdr)
        if ($m.Success) {
            $lvl = $m.Groups[1].Value
            $cnt[$lvl]++
            if ($lvl -eq 'ERROR') {
                $msg = ($l.Substring($m.Length) -replace '\d+', '#')
                if ($msg.Length -gt 200) { $msg = $msg.Substring(0, 200) }
                if ($freq.ContainsKey($msg)) { $freq[$msg]++ } else { $freq[$msg] = 1 }
            }
        }
        foreach ($s in $subs) { if ($l.IndexOf($s, [StringComparison]::Ordinal) -ge 0) { $hits[$s]++ } }
        if ($null -eq $harmLine -and $l.IndexOf('Harmony library v', [StringComparison]::Ordinal) -ge 0) { $harmLine = $l }
        if ($null -eq $startedLine -and $l -match '^\s*Started:') { $startedLine = $l }
        if (-not $logClosed -and $l.IndexOf('Log closed', [StringComparison]::Ordinal) -ge 0) { $logClosed = $true }
    }
    $top = @($freq.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 25 | ForEach-Object { [ordered]@{ count = $_.Value; message = $_.Key } })

    # 9
    $step = '9 verdict and report'
    $misses = New-Object System.Collections.ArrayList
    if ($cnt.ERROR -ge 200) { [void]$misses.Add("ERROR headers $($cnt.ERROR) >= 200") }
    if ($hits['HarmonyInfix'] -gt 0) { [void]$misses.Add("HarmonyInfix $($hits['HarmonyInfix'])") }
    if ($hits['DivideByZero'] -gt 0) { [void]$misses.Add("DivideByZero $($hits['DivideByZero'])") }
    if ($hits['ImageOpt'] -gt 0) { [void]$misses.Add("ImageOpt $($hits['ImageOpt'])") }
    if ($null -eq $harmLine) { [void]$misses.Add('no Harmony library line') }
    elseif ($harmLine -notmatch 'Harmony library v3\.') { [void]$misses.Add('Harmony library not 3.x') }
    if ($stopReason -eq 'process_exited' -or $stopReason -eq 'timeout_30min') { [void]$misses.Add("no clean load ($stopReason)") }
    if ($misses.Count -eq 0) { $verdict = 'VERDICT: PASS'; $exitCode = 0 }
    else { $verdict = 'VERDICT: FAIL (' + ($misses -join '; ') + ')'; $exitCode = 1 }

    $result = [ordered]@{
        stamp = $stamp; runDir = $runDir; stopReason = $stopReason
        secondsAtStopDecision = $elapsedAtStop; totalSeconds = $totalSec
        started = $startedLine; logClosed = $logClosed; harmonyLine = $harmLine
        headerCounts = [ordered]@{ ERROR = $cnt.ERROR; WARN = $cnt.WARN; INFO = $cnt.INFO }
        substringHitLines = $hits; topErrors = $top; verdict = $verdict
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDir 'result.json') -Encoding UTF8

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("Run:          $stamp")
    [void]$sb.AppendLine("Stop reason:  $stopReason (decision at ${elapsedAtStop}s, process killed at ${totalSec}s since launch)")
    [void]$sb.AppendLine("Started line: $startedLine")
    [void]$sb.AppendLine("Log closed:   $logClosed")
    [void]$sb.AppendLine("Harmony line: $harmLine")
    [void]$sb.AppendLine("Header lines: ERROR $($cnt.ERROR) / WARN $($cnt.WARN) / INFO $($cnt.INFO)")
    [void]$sb.AppendLine('Substring hits (lines containing):')
    foreach ($s in $subs) { [void]$sb.AppendLine(("  {0,-34} {1}" -f $s, $hits[$s])) }
    [void]$sb.AppendLine('Top 25 ERROR messages (digits -> #, 200 chars):')
    foreach ($t in $top) { [void]$sb.AppendLine(("  {0,6}  {1}" -f $t.count, $t.message)) }
    [void]$sb.AppendLine($verdict)
    [System.IO.File]::WriteAllText((Join-Path $runDir 'summary.txt'), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host $sb.ToString()
    Write-Host $verdict
}
catch {
    $exitCode = 2
    Write-Host "HARNESS ERROR at step [$step]: $($_.Exception.Message)"
    if ($null -ne $runDir -and (Test-Path -LiteralPath $runDir)) {
        try { "HARNESS ERROR at step [$step]: $($_ | Out-String)" | Set-Content -LiteralPath (Join-Path $runDir 'harness_error.txt') -Encoding UTF8 } catch {}
    }
}
finally {
    # only kill if we got past preflight (runDir set)
    if ($null -ne $runDir) {
        try { Get-Game | ForEach-Object { Stop-Process -Id $_.Id -Force } } catch {}
    }
}
exit $exitCode
