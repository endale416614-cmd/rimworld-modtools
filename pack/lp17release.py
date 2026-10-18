"""Build the LocalPack 1.7 release tree in the Homebrew 1.7 installer format.

  py -I lp17release.py PACK OUT CONFIGDIR "GAMEBUILD"

PACK      LocalPack source (Mods\\NN-cat__packageid folders, release.json with the category titles in "packs")
OUT       new, empty build folder on the same volume as PACK (Mods files are hard links, not copies)
CONFIGDIR the RimWorld Config folder whose ModsConfig.xml holds the load order to ship (the tested live list)

Writes OUT\\Mods (every pack folder in the load order; *.orig / *.pre17 backups left out), OUT\\Config
(ModsConfig.xml + the mod settings files of shipped folders), OUT\\release.json (installer schema: one 'keep'
entry per folder with files/bytes/treeHash/dlls exactly like the installer's Util.Measure/TreeHash, plus
"packs" categories and each entry's "category") and OUT\\LoadOrder.txt / CATEGORIES.txt. Then run hb17zip.py.
"""
import datetime, json, os, re, shutil, sys, xml.etree.ElementTree as ET
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hb17build import tree, sha_file

BACKUP = ('.orig', '.pre17')
# settings that are caches of the installed mod set (replaying them is wrong) or that hold machine paths
SKIP_SETTINGS = ('FasterGameLoadingMod',)
PERSONAL = re.compile(r'[A-Za-z]:\\\\?(Users|Steam|Rimworld Mod|Development)|AppData|LocalLow', re.I)


def folder_key(f):
    return f.split('__', 1)[1].lower().removesuffix('_3005289691')  # RimWorld Together's folder must end in its id


def main(pack, out, cfgdir, gamebuild):
    assert not os.path.exists(out) or not os.listdir(out), 'OUT must be new or empty'
    src = json.load(open(os.path.join(pack, 'release.json'), encoding='utf-8'))
    titles = {p['key']: p['title'] for p in src['packs']}
    pmods = os.path.join(pack, 'Mods')
    folders = {folder_key(f): f for f in os.listdir(pmods)}
    mc = open(os.path.join(cfgdir, 'ModsConfig.xml'), 'rb').read()
    order = [x.lower() for x in re.findall(rb'<li>([^<]+)</li>', mc.split(b'<activeMods>')[1].split(b'</activeMods>')[0])]
    order = [x.decode() for x in order]
    dlc = [x for x in order if x.startswith('ludeon.')]
    missing = [x for x in order if x not in dlc and x not in folders]
    assert not missing, 'in load order but not in the pack: ' + ', '.join(missing)
    extra = sorted(f for k, f in folders.items() if k not in order)
    assert not extra, 'in the pack but not in the load order: ' + ', '.join(extra)
    for k, f in folders.items():  # folder name must match the mod's real packageId
        pid = (ET.parse(os.path.join(pmods, f, 'About', 'About.xml')).getroot().findtext('packageId') or '').strip().lower()
        assert pid == k, (f, pid)

    omods = os.path.join(out, 'Mods'); os.makedirs(omods)
    for x in order:
        if x in dlc: continue
        f = folders[x]
        for dp, dn, fn in os.walk(os.path.join(pmods, f)):
            rel = os.path.relpath(dp, pmods)
            os.makedirs(os.path.join(omods, rel), exist_ok=True)
            for n in fn:
                if n.endswith(BACKUP): continue
                os.link(os.path.join(dp, n), os.path.join(omods, rel, n))

    ocfg = os.path.join(out, 'Config'); os.makedirs(ocfg)
    shutil.copy2(os.path.join(cfgdir, 'ModsConfig.xml'), os.path.join(ocfg, 'ModsConfig.xml'))
    shipped = set(os.listdir(omods)); skipped = []
    for n in sorted(os.listdir(cfgdir)):
        owner = next((f for f in shipped if n.startswith('Mod_' + f + '_') and n.endswith('.xml')), None)
        if not owner: continue
        txt = open(os.path.join(cfgdir, n), encoding='utf-8', errors='replace').read()
        if n[len('Mod_' + owner + '_'):-4] in SKIP_SETTINGS or PERSONAL.search(txt): skipped.append(n); continue
        shutil.copy2(os.path.join(cfgdir, n), os.path.join(ocfg, n))

    entries, counts = [], {}
    for x in order:
        if x in dlc: continue
        f = folders[x]; cat = f.split('__', 1)[0]; counts[cat] = counts.get(cat, 0) + 1
        a = ET.parse(os.path.join(omods, f, 'About', 'About.xml')).getroot()
        files, size, th, dlls = tree(os.path.join(omods, f))
        wid = os.path.join(omods, f, 'About', 'PublishedFileId.txt')
        entries.append({'name': (a.findtext('name') or f).strip(), 'kind': 'keep', 'folder': f, 'category': cat,
                        'packageId': x, 'packageIds': [x], 'memberFolders': {},
                        'workshopId': open(wid).read().strip() if os.path.exists(wid) else '',
                        'files': files, 'bytes': size, 'treeHash': th, 'dlls': dlls})
    rel = {'name': 'LocalPack', 'version': src['version'], 'gameVersion': gamebuild.split()[0],
           'gameBuild': gamebuild, 'built': datetime.datetime.now().replace(microsecond=0).isoformat(), 'dlcs': dlc,
           'packs': [{'key': k, 'title': titles[k], 'count': counts[k]} for k in sorted(counts)],
           'removed': [{k: v for k, v in r.items() if k != 'source'} for r in src.get('removed', [])],
           'entries': entries,
           'config': {n: sha_file(os.path.join(ocfg, n)) for n in sorted(os.listdir(ocfg), key=lambda s: (s != 'ModsConfig.xml', s))}}
    rel['files'] = sum(e['files'] for e in entries); rel['bytes'] = sum(e['bytes'] for e in entries)
    json.dump(rel, open(os.path.join(out, 'release.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    with open(os.path.join(out, 'LoadOrder.txt'), 'w', encoding='utf-8', newline='\r\n') as w:
        w.write('# LocalPack v1.7 load order (as shipped in Config/ModsConfig.xml). packageId  # category\n')
        for x in order: w.write(x + ('' if x in dlc else '  # ' + folders[x].split('__', 1)[0]) + '\n')
    with open(os.path.join(out, 'CATEGORIES.txt'), 'w', encoding='utf-8', newline='\r\n') as w:
        for k in sorted(counts):
            w.write(f'== {k}  {titles[k]} ({counts[k]})\n')
            for e in entries:
                if e['category'] == k: w.write(f"   {e['name']}  [{e['packageId']}]\n")
    print(f"entries={len(entries)} dlc={len(dlc)} files={rel['files']} bytes={rel['bytes']} settings={len(rel['config']) - 1} settings_skipped={skipped}")


if __name__ == '__main__':
    if len(sys.argv) != 5: sys.exit(__doc__)
    main(*sys.argv[1:5])
