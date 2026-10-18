"""Tag local mod forks as 1.7: add <li>1.7</li> to About.xml supportedVersions.
LoadFolders.xml is left alone on purpose: copying the newest entry as v1.7 stalled the
2026-10-18 launch ~5 min before Harmony loaded and changed which content loaded; the
game's own fallback to the newest older entry is the tested path.
Dry run by default. --apply edits in place, keeping <file>.pre17 backups (never overwritten).
Usage: py -I tag17.py MODSDIR [MODSDIR...] [--apply]"""
import os, re, sys, xml.etree.ElementTree as ET

VER = b'1.7'
apply = '--apply' in sys.argv
dirs = [a for a in sys.argv[1:] if a != '--apply']

def parses(data):
    try: ET.fromstring(data); return True
    except ET.ParseError: return False

def tag_about(data):
    if re.search(rb'<li>\s*1\.7\s*</li>', data): return None
    m = re.search(rb'<supportedVersions>(.*?)</supportedVersions>', data, re.I | re.S)
    if not m: return None
    i = data.rfind(b'</li>', m.start(1), m.end(1))
    if i < 0: return None
    i += len(b'</li>')
    li = data.rfind(b'<li>', m.start(1), i)
    line = data.rfind(b'\n', 0, li) + 1
    indent = data[line:li]
    if indent.strip(b' \t') or li <= m.start(1):  # inline list: stay on the same line
        ins = b'<li>' + VER + b'</li>'
    else:
        nl = b'\r\n' if b'\r\n' in data else b'\n'
        ins = nl + indent + b'<li>' + VER + b'</li>'
    return data[:i] + ins + data[i:]

def edit(path, fn, stats, key):
    if not os.path.isfile(path): return
    data = open(path, 'rb').read()
    new = fn(data)
    if new is None: return
    if not parses(new):
        stats['bad'].append(path); return
    stats[key] += 1
    if apply:
        bak = path + '.pre17'
        if not os.path.exists(bak): open(bak, 'wb').write(data)
        open(path, 'wb').write(new)

for root in dirs:
    stats = {'about': 0, 'bad': [], 'mods': 0}
    for f in sorted(os.listdir(root)):
        d = os.path.join(root, f)
        if not os.path.isdir(d): continue
        stats['mods'] += 1
        edit(os.path.join(d, 'About', 'About.xml'), tag_about, stats, 'about')
    print(('APPLIED ' if apply else 'DRY RUN ') + root,
          f"mods={stats['mods']} about_tagged={stats['about']} unparseable_after_edit={len(stats['bad'])}")
    for b in stats['bad']: print('  SKIPPED (would not parse):', b)

if __name__ == '__main__' and not dirs:
    # self-check
    a = b'<ModMetaData>\n  <supportedVersions>\n    <li>1.6</li>\n  </supportedVersions>\n</ModMetaData>'
    out = tag_about(a); assert b'    <li>1.7</li>' in out and parses(out) and tag_about(out) is None
    print('self-check ok')
