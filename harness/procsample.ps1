# Samples the RimWorld process from outside while a harness run is in progress, to tell a CPU-bound stall from disk
# reads or an idle wait. Waits up to -WaitSec for the process, then writes one row every -EverySec until it exits.
# Columns: time, cpu seconds (all threads, cumulative), working set MB, private MB, threads, MB read, MB written,
# GPU memory used MB (whole card, from nvidia-smi).
param(
    [Parameter(Mandatory)][string]$Out,
    [int]$EverySec = 2,
    [int]$WaitSec = 120
)
$deadline = (Get-Date).AddSeconds($WaitSec)
$proc = $null
while (-not $proc -and (Get-Date) -lt $deadline) { $proc = Get-Process -Name RimWorld -ErrorAction SilentlyContinue | Select-Object -First 1; if (-not $proc) { Start-Sleep -Milliseconds 500 } }
if (-not $proc) { 'RimWorld did not start'; exit 1 }
"time`tcpu_s`tws_mb`tprivate_mb`tthreads`tread_mb`twrite_mb`tgpu_mb" | Set-Content -LiteralPath $Out -Encoding ascii
while (-not $proc.HasExited) {
    $proc.Refresh()
    $io = Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)" -ErrorAction SilentlyContinue
    $gpu = (& nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits 2>$null | Select-Object -First 1)
    if ($proc.HasExited -or -not $io) { break }
    ('{0:HH:mm:ss}' -f (Get-Date)), [math]::Round($proc.TotalProcessorTime.TotalSeconds, 1), [int]($proc.WorkingSet64 / 1MB), [int]($proc.PrivateMemorySize64 / 1MB),
        $proc.Threads.Count, [int]($io.ReadTransferCount / 1MB), [int]($io.WriteTransferCount / 1MB), "$gpu".Trim() -join "`t" | Add-Content -LiteralPath $Out -Encoding ascii
    Start-Sleep -Seconds $EverySec
}
