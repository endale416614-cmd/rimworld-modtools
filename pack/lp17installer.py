"""Make the LocalPack 1.7 installer from the Homebrew 1.7 installer .bat (output of hb17installer.py).
The script part before ##PAYLOAD is edited by exact replacements; the embedded tools zip after it is re-packed with
rebranded tool .bats and the LocalPack README.
Every replacement must match exactly the expected number of times, so a changed installer fails loudly.
The C# helper namespace (HomebrewModpackSetup) is internal and kept.
  py -I lp17installer.py HOMEBREW17.bat OUT.bat BUILDSTAMP RETIRED_IDS_COMMA_SEPARATED README.txt
"""
import re, sys

src, dst, stamp, retired, readme = sys.argv[1:6]
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
# The extractor counted and hashed every written file, while Util.Measure/TreeHash skip a .dds next to a same-name
# picture. Homebrew shipped no such twins; LocalPack has 1,846 author-made ones. Make the extractor skip them too.
SORT = '                    lines.Sort(delegate (KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.CompareOrdinal(a.Key, b.Key); });'
TWINS = '\r\n'.join([
    '                    // LocalPack: skip .dds twins exactly like Util.Measure/TreeHash (the files are still written)',
    '                    HashSet<string> rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);',
    '                    foreach (KeyValuePair<string, string> kv in lines) rels.Add(kv.Key);',
    '                    HashSet<string> twins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);',
    '                    foreach (string k in rels)',
    '                        if (k.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))',
    '                            foreach (string x in new string[] { ".png", ".jpg", ".jpeg", ".psd" }) if (rels.Contains(k.Substring(0, k.Length - 4) + x)) { twins.Add(k); break; }',
    '                    foreach (ZipArchiveEntry z in list) if (twins.Contains(z.FullName.Substring(5 + folder.Length + 1))) fbytes -= z.Length;',
    '                    lines.RemoveAll(delegate (KeyValuePair<string, string> kv) { return twins.Contains(kv.Key); });',
    ''])
R.append((SORT, TWINS + SORT, 1))
for a, b, n in R:
    c = head.count(a)
    if c != n: sys.exit(f'expected {n} match(es), found {c}: {a}')
    head = head.replace(a, b)
head, k = re.subn(r"^\$RetiredIds = @\([^\r\n]*\)(?=\r?$)", lambda m: f"$RetiredIds = @({ids})", head, flags=re.M)
if k != 1: sys.exit(f'RetiredIds line matched {k} times')
left = [l for l in head.splitlines() if re.search(r'homebrew', l, re.I) and 'HomebrewModpackSetup' not in l]
if left: sys.exit('unreplaced Homebrew text:\n' + '\n'.join(left))

# The payload is a base64 zip of the Desktop tools (Undo last update, Send bug report, README). They carry the same
# product/tool-folder names, so rebrand them too and swap in the LocalPack README (argument 5).
import base64, io, zipfile
b64 = b''.join(tail[len(b'##PAYLOAD'):].split())
zin = zipfile.ZipFile(io.BytesIO(base64.b64decode(b64)))
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as zout:
    for info in zin.infolist():
        data = zin.read(info)
        if info.filename.lower().startswith('tools/readme'):
            info.filename = 'tools/README - LocalPack.txt'; data = open(readme, 'rb').read()
        elif info.filename.lower().endswith('.bat'):
            t = data.decode('utf-8')
            for a, b in (('Homebrew-Modpack', 'LocalPack'), ('Homebrew Modpack', 'LocalPack'), ('HomebrewPackCore', 'LocalPackCore')):
                t = t.replace(a, b)
            t = re.sub(r'HomebrewModpack(?!Setup)', 'LocalPack', t)
            bad = [l for l in t.splitlines() if re.search(r'homebrew', l, re.I) and 'HomebrewModpackSetup' not in l]
            if bad: sys.exit(f'unreplaced Homebrew text in {info.filename}:\n' + '\n'.join(bad))
            data = t.encode('utf-8')
        zout.writestr(info, data)
enc = base64.b64encode(buf.getvalue())
tail = b'##PAYLOAD\r\n' + b''.join(enc[i:i + 120] + b'\r\n' for i in range(0, len(enc), 120))
open(dst, 'wb').write(head.encode('utf-8') + tail)
print('ok', dst, 'build', stamp, 'retired', ids, 'tools', [i.filename for i in zipfile.ZipFile(buf).infolist()])
