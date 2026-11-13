"""Remove translation entries the game reports as load errors, using the hotfix's TranslationErrors-English.tsv.

Mods ship English text for defs that never load here (Combat Extended only, older game versions), and each entry
counts toward the "Translation data has N errors" warning. Originals are copied to the quarantine folder first.
A workshop update restores the files; run this again if the count comes back.

py -I prunelang.py [--apply]
"""
import csv
import os
import re
import shutil
import sys

TSV = os.path.expandvars(r'%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\TranslationErrors-English.tsv')
BACKUP = r'J:\Rimworld Mod\_quarantine\lang-prune-20261113'
WORKSHOP = r'F:\Steam\steamapps\workshop\content\294100'
APPLY = '--apply' in sys.argv

KEY = re.compile(r"(?:to match|Couldn't inject) (\S+?)(?: \(| into )")
BAD_DIR = re.compile(r"Error loading language from \(([^,]+), .*: dir (\S+) doesn't correspond")
DUP = re.compile(r'Duplicate keyed translation key: (\S+) in language')


def backup(path):
    rel = os.path.relpath(path, WORKSHOP)
    dest = os.path.join(BACKUP, rel)
    if os.path.exists(dest):
        return
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.isdir(path):
        shutil.copytree(path, dest)
    else:
        shutil.copy2(path, dest)


def entry(key):
    k = re.escape(key.encode('utf-8'))
    return re.compile(rb'^[ \t]*(?:<' + k + rb'>.*?</' + k + rb'>|<' + k + rb'\s*/>)[ \t]*\r?\n?', re.M | re.S)


rows = list(csv.DictReader(open(TSV, encoding='utf-8'), delimiter='\t', quoting=csv.QUOTE_NONE))
edits, removed, unhandled = {}, 0, []
for r in rows:
    text = r['text']
    m = BAD_DIR.search(text)
    if m:
        folder = os.path.join(m.group(1), 'DefInjected', m.group(2))
        print('dir', folder, os.path.isdir(folder))
        if APPLY and os.path.isdir(folder):
            backup(folder)
            shutil.rmtree(folder)
        continue
    m = DUP.search(text)
    if m:
        path = r['file']
        data = open(path, 'rb').read()
        hits = list(entry(m.group(1)).finditer(data))
        print('duplicate', m.group(1), 'in file', len(hits), path)
        if len(hits) > 1:
            last = hits[-1]
            edits[path] = data[:last.start()] + data[last.end():]
            removed += 1
        else:
            unhandled.append(text)
        continue
    m = KEY.search(text)
    if not m or not r['file']:
        unhandled.append(text)
        continue
    hit = False
    for path in r['file'].split(' | '):
        path = path.strip()
        if not os.path.isfile(path):
            continue
        data = edits.get(path) or open(path, 'rb').read()
        new, n = entry(m.group(1)).subn(b'', data)
        if n:
            edits[path] = new
            hit = True
            removed += n
    if not hit:
        unhandled.append(text)
print('rows', len(rows), 'entries removed', removed, 'files', len(edits))
for t in unhandled:
    print('UNHANDLED', t[:200])
if APPLY:
    for path, data in edits.items():
        backup(path)
        open(path, 'wb').write(data)
    print('written; originals in', BACKUP)
