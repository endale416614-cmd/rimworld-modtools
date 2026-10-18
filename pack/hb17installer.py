"""Patch the Homebrew installer .bat for RimWorld 1.7 (Waylith ships RimWorld.exe, not RimWorldWin64.exe).
Only the script part before the ##PAYLOAD line is edited; the embedded tools zip after it is copied unchanged.
Every replacement must match exactly once, so a changed installer fails loudly instead of half-patching.
  py -I hb17installer.py IN.bat OUT.bat GAMEBUILD BUILDSTAMP
"""
import sys

src, dst, gamebuild, stamp = sys.argv[1:5]
data = open(src, 'rb').read()
cut = data.index(b'\n##PAYLOAD') + 1
head, tail = data[:cut].decode('utf-8'), data[cut:]
DROPPED = ['homebrew.modpack.core', 'zetrith.prepatcher', 'dev.soeur.imageopt', 'v1024.visibleerrorlogs',
           'telefonmast.graphicssettings', 'ferny.nomodlistonloading', 'sov.det.surrogatemech']
R = [
    ("(20260928-9402f99)", f"({stamp})"),
    ("$Build = '20260928-9402f99'", f"$Build = '{stamp}'"),
    ("function Test-GameDir([string]$d) { return $d -and [IO.File]::Exists((Join-Path $d 'RimWorldWin64.exe')) }",
     "function Test-GameDir([string]$d) { return $d -and ([IO.File]::Exists((Join-Path $d 'RimWorld.exe')) -or [IO.File]::Exists((Join-Path $d 'RimWorldWin64.exe'))) }"),
    ("return [bool](Get-Process -Name 'RimWorldWin64' -ErrorAction SilentlyContinue)",
     "return [bool](Get-Process -Name 'RimWorld', 'RimWorldWin64' -ErrorAction SilentlyContinue)"),
    ("$GameVersionWanted = '1.6'", "$GameVersionWanted = '1.7'"),
    ("$PackGameBuild = '1.6.4871 rev590'", f"$PackGameBuild = '{gamebuild}'"),
    ("$RetiredIds = @('cc.rimworld.nicerpginventorycompat', 'raccooncuddler.showhair', 'sandy.rpgstyleinventory.avilmask.revamped')",
     "$RetiredIds = @('cc.rimworld.nicerpginventorycompat', 'raccooncuddler.showhair', 'sandy.rpgstyleinventory.avilmask.revamped', "
     + ", ".join(f"'{i}'" for i in DROPPED) + ")"),
    ("'Choose your RimWorld folder (the one that contains RimWorldWin64.exe)'", "'Choose your RimWorld folder (the one that contains RimWorld.exe)'"),
    ("'That folder does not contain RimWorldWin64.exe. Please choose the RimWorld folder.'", "'That folder does not contain RimWorld.exe. Please choose the RimWorld folder.'"),
    ("else { Start-Process -FilePath (Join-Path $g 'RimWorldWin64.exe') -WorkingDirectory $g }",
     "else { $exe = if ([IO.File]::Exists((Join-Path $g 'RimWorld.exe'))) { 'RimWorld.exe' } else { 'RimWorldWin64.exe' }; Start-Process -FilePath (Join-Path $g $exe) -WorkingDirectory $g }"),
]
for old, new in R:
    n = head.count(old)
    if n != 1: sys.exit(f'expected 1 match, found {n}: {old[:80]}')
    head = head.replace(old, new)
left = [l for l in head.splitlines() if 'RimWorldWin64' in l and 'RimWorld.exe' not in l and "'RimWorld'," not in l]
if left: sys.exit('unpatched RimWorldWin64 lines:\n' + '\n'.join(left))
open(dst, 'wb').write(head.encode('utf-8') + tail)
print(f'patched {len(R)} spots; payload {len(tail)} bytes unchanged')
