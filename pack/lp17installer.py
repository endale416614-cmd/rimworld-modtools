"""Make the LocalPack 1.7 installer from the Homebrew 1.7 installer .bat (output of hb17installer.py).
Only the script part before the ##PAYLOAD line is edited; the embedded tools zip after it is copied unchanged.
Every replacement must match exactly the expected number of times, so a changed installer fails loudly.
The C# helper namespace (HomebrewModpackSetup) is internal and kept.
  py -I lp17installer.py HOMEBREW17.bat OUT.bat BUILDSTAMP RETIRED_IDS_COMMA_SEPARATED
"""
import re, sys

src, dst, stamp, retired = sys.argv[1:5]
data = open(src, 'rb').read()
cut = data.index(b'\n##PAYLOAD') + 1
head, tail = data[:cut].decode('utf-8'), data[cut:]
old_build = re.search(r"\$Build = '([^']+)'", head).group(1)
ids = ', '.join(f"'{i.strip().lower()}'" for i in retired.split(',') if i.strip())
R = [
    ('title Homebrew Modpack v1.7 - Installer', 'title LocalPack v1.7 - Installer', 1),
    (f'rem Homebrew Modpack v1.7 ({old_build}) - one-click installer. Double-click it; keep Modpack-v1.7.data.zip next to it.',
     f'rem LocalPack v1.7 ({stamp}) - one-click installer. Double-click it; keep LocalPack-v1.7.data.zip next to it.', 1),
    ('# Homebrew Modpack - shared code', '# LocalPack - shared code', 1),
    (f"$Build = '{old_build}'", f"$Build = '{stamp}'", 1),
    ("$Product = 'Homebrew Modpack'", "$Product = 'LocalPack'", 1),
    ("$ToolDirName = 'HomebrewModpack'", "$ToolDirName = 'LocalPack'", 1),
    ('<game>\\HomebrewModpack\\', '<game>\\LocalPack\\', 2),
    ("'^HomebrewModpack\\\\Backups", "'^LocalPack\\\\Backups", 1),
    ("('HomebrewModpack-' + [guid]", "('LocalPack-' + [guid]", 1),
    ("'Mods\\HomebrewPackCore\\Version.txt'", "'Mods\\LocalPackCore\\Version.txt'", 1),  # never present: no cross-detection of Homebrew
    ("$DataName = 'Modpack-v1.7.data.zip'", "$DataName = 'LocalPack-v1.7.data.zip'", 1),
    ('double-click "Install Homebrew Modpack" there.', 'double-click "Install LocalPack" there.', 1),
    ("(the 'Install Homebrew Modpack' file)", "(the 'Install LocalPack' file)", 1),
]
for a, b, n in R:
    c = head.count(a)
    if c != n: sys.exit(f'expected {n} match(es), found {c}: {a}')
    head = head.replace(a, b)
head, k = re.subn(r"^\$RetiredIds = @\([^\r\n]*\)(?=\r?$)", lambda m: f"$RetiredIds = @({ids})", head, flags=re.M)
if k != 1: sys.exit(f'RetiredIds line matched {k} times')
left = [l for l in head.splitlines() if re.search(r'homebrew', l, re.I) and 'HomebrewModpackSetup' not in l]
if left: sys.exit('unreplaced Homebrew text:\n' + '\n'.join(left))
open(dst, 'wb').write(head.encode('utf-8') + tail)
print('ok', dst, 'build', stamp, 'retired', ids)
