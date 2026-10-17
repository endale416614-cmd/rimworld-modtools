# rimstack-sort.ps1 : run RimStack's headless --check on a COPY of the live ModsConfig.xml,
# then show the load-order moves RimStack's Sort produced. With -Apply, write the sorted
# order to the live ModsConfig.xml and to the pack's ModsConfig.xml / LoadOrder.txt.
# Windows PowerShell 5.1. Refuses while RimWorld or RimSort runs (RimStack refuses to save then too).
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$app  = 'F:\Rimworld Mod\RimStack\app\rimstack.exe'
$pack = 'F:\Rimworld Mod\LocalPack-v1.7'
$live = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml'
$chk  = 'F:\Rimworld Mod\RimStack\check'
$run = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in 'RimWorld','RimWorldWin64','RimSort' })
if ($run.Count -gt 0) { throw ('Close these first: ' + (($run | ForEach-Object { $_.ProcessName } | Sort-Object -Unique) -join ', ')) }
if (Test-Path -LiteralPath $chk) { Remove-Item -LiteralPath $chk -Recurse -Force }
New-Item -ItemType Directory -Path "$chk\config\Config","$chk\data","$chk\settings\RimStack" -Force | Out-Null
Copy-Item -LiteralPath $live -Destination "$chk\config\Config\ModsConfig.xml"
Copy-Item -LiteralPath $live -Destination "$chk\original.xml"
'{ "language": "English", "setupDone": "yes" }' | Set-Content -LiteralPath "$chk\settings\RimStack\settings.json" -Encoding ascii
$env:RIMSTACK_CONFIG = "$chk\config"; $env:XDG_DATA_HOME = "$chk\data"; $env:XDG_CONFIG_HOME = "$chk\settings"; $env:QT_FORCE_STDERR_LOGGING = '1'
$p = Start-Process -FilePath $app -ArgumentList '--check' -WorkingDirectory (Split-Path $app) -RedirectStandardOutput "$chk\out.log" -RedirectStandardError "$chk\err.log" -PassThru
if (-not $p.WaitForExit(600000)) { Stop-Process -Id $p.Id -Force; throw 'RimStack --check timed out after 10 min' }
$fails = @(Select-String -LiteralPath "$chk\err.log" -Pattern 'check FAIL' | ForEach-Object { $_.Line })
if ($fails.Count -gt 0) { $fails | Write-Host; throw "RimStack --check reported $($fails.Count) failure(s); see $chk\err.log" }
$saved = "$chk\config\Config\ModsConfig.xml"
$before = @([regex]::Matches((Get-Content -LiteralPath "$chk\original.xml" -Raw), '<li>([^<]+)</li>') | ForEach-Object { $_.Groups[1].Value })
$after  = @([regex]::Matches((Get-Content -LiteralPath $saved -Raw), '<li>([^<]+)</li>') | ForEach-Object { $_.Groups[1].Value })
$known = 5  # trailing <knownExpansions> entries
$bActive = $before[0..($before.Count-1-$known)]; $aActive = $after[0..($after.Count-1-$known)]
$added = @($aActive | Where-Object { $bActive -notcontains $_ }); $removed = @($bActive | Where-Object { $aActive -notcontains $_ })
$pos = @{}; for ($i=0; $i -lt $bActive.Count; $i++) { $pos[$bActive[$i]] = $i }
$moves = 0; $lines = New-Object System.Collections.Generic.List[string]
for ($i=0; $i -lt $aActive.Count; $i++) { $id = $aActive[$i]; if ($pos.ContainsKey($id) -and $pos[$id] -ne $i) { $moves++; if ($lines.Count -lt 60) { $lines.Add(('{0,4} -> {1,4}  {2}' -f $pos[$id], $i, $id)) } } }
"RimStack sort: $($bActive.Count) mods before, $($aActive.Count) after, $moves moved, $($added.Count) added, $($removed.Count) removed"
if ($added.Count)   { "ADDED:";   $added   | ForEach-Object { "  $_" } }
if ($removed.Count) { "REMOVED:"; $removed | ForEach-Object { "  $_" } }
$lines | ForEach-Object { $_ }
Copy-Item -LiteralPath $saved -Destination "$chk\sorted.xml" -Force
if (-not $Apply) { "Dry run. Sorted copy: $chk\sorted.xml  (rerun with -Apply to install it)"; exit 0 }
if ($added.Count -or $removed.Count) { throw 'Refusing to apply: the sorted list adds or removes mods, not just reorders. Inspect sorted.xml.' }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
Copy-Item -LiteralPath $live -Destination ($live -replace 'ModsConfig\.xml$', "ModsConfig.backup-$stamp.xml")
Copy-Item -LiteralPath $saved -Destination $live -Force
Copy-Item -LiteralPath $saved -Destination "$pack\ModsConfig.xml" -Force
$lo = "# LocalPack v1.7 load order (RimStack sort $stamp). One packageId per line.`n" + (($aActive | ForEach-Object { $_ }) -join "`n") + "`n"
Set-Content -LiteralPath "$pack\LoadOrder.txt" -Value $lo -Encoding UTF8
"Applied to live ModsConfig.xml (backup ModsConfig.backup-$stamp.xml) and to $pack"
