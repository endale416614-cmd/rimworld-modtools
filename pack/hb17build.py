"""Rebuild the Homebrew Modpack data for RimWorld 1.7 (Waylith, .NET 10) without Pack Core / Prepatcher.

Pack Core needs Prepatcher (Mono-only) to expose HBP_* pack folders as their member mods, so on 1.7 every
pack is flattened: each member becomes a normal top-level mod folder named like its Members/<folder> (mod
settings are keyed by that name, so Config/Mod_<folder>_*.xml stay valid).
- workshop members and workshop 'keep' folders: the current workshop copy (it has the original textures,
  its own LoadFolders.xml and, where the author has one, the 1.7 port), slimmed like audit/buildpack.py.
- local members: the pack's copy, its LoadFolders entry rebuilt from the pack's LoadFolders.xml, and its
  textures decoded from the pack's BC7 bundle (bundlex) for every bundled texture no workshop member provides.
- DROP: mods that fail on 1.7 (see LocalPack audit) plus Pack Core.

  py -I hb17build.py flatten SRC OUT BUNDLEX.exe     SRC = extracted Modpack-v1.7.data.zip, OUT = new tree
  py -I hb17build.py release SRC OUT GAMEBUILD        writes OUT/Config/ModsConfig.xml and OUT/release.json
Run refcheck / patch17 / tag17.py on OUT/Mods between the two steps.
"""
import hashlib, json, os, re, shutil, subprocess, sys, tempfile, xml.etree.ElementTree as ET

WS = r'F:\Steam\steamapps\workshop\content\294100'
DROP = {
    'homebrew.modpack.core': 'Pack Core: needs Prepatcher, which is Mono-only',
    'zetrith.prepatcher': 'Mono-only (Mono assembly internals); ships Harmony 2.4.2 that shadows Harmony 3',
    'dev.soeur.imageopt': 'native texture loader crashes on every texture on 1.7',
    'v1024.visibleerrorlogs': 'Harmony patch emits invalid IL on CoreCLR',
    'telefonmast.graphicssettings': 'texture hook fails on 1.7: thousands of missing-texture errors',
    'ferny.nomodlistonloading': '1.6-only loading-screen patch; DivideByZero in GameplayTipWindow on 1.7',
    'sov.det.surrogatemech': 'removed from the 1.7 LocalPack audit',
}
verdir = re.compile(r'^v?\d+\.\d+$')


# same slimming as audit/buildpack.py: keep the best version folder (1.7, else 1.6, else newest) and
# whatever the newest LoadFolders entry <= 1.7 names; drop Source/.git and other version folders
def keep_version_dirs(src):
    vers = [x for x in os.listdir(src) if verdir.match(x) and os.path.isdir(os.path.join(src, x))]
    keep = set()
    if vers:
        nums = sorted(vers, key=lambda v: tuple(int(t) for t in v.lstrip('v').split('.')))
        best = [v for v in nums if v.lstrip('v') in ('1.7', '1.6')]
        keep = {best[-1]} if best else {nums[-1]}
    lf = os.path.join(src, 'LoadFolders.xml')
    if os.path.isfile(lf):
        try:
            ents = {}
            for e in ET.parse(lf).getroot():
                try: ents[tuple(int(t) for t in e.tag.lstrip('v').split('.'))] = [(c.text or '').strip() for c in e]
                except ValueError: pass
            cands = [k for k in ents if k <= (1, 7)]
            if cands:
                for f in ents[max(cands)]:
                    top = f.replace('\\', '/').strip('/').split('/')[0]
                    if top and top != '.': keep.add(top)
        except ET.ParseError: pass
    return keep, vers


def copy_slim(src, dst):
    keep, vers = keep_version_dirs(src)
    for dp, dn, fn in os.walk(src):
        rel = os.path.relpath(dp, src); top = rel.split(os.sep)[0]
        if top in ('Source', '.git', '.vs', '.vscode') or (top in vers and top not in keep): dn[:] = []; continue
        low = {f.lower() for f in fn}
        for f in fn:
            # .dds next to a same-name picture (Image Opt cache): the installer counts it on extract but not in the release
            stem, ext = os.path.splitext(f)
            if ext.lower() == '.dds' and any((stem + e).lower() in low for e in ('.png', '.jpg', '.jpeg', '.psd')): continue
            d = os.path.join(dst, rel, f)
            os.makedirs(os.path.dirname(d), exist_ok=True); shutil.copy2(os.path.join(dp, f), d)


def tex_keys(root):
    """lower-case texture paths (relative to any Textures dir, no extension) a mod folder provides"""
    out = set()
    for dp, dn, fn in os.walk(root):
        parts = os.path.relpath(dp, root).replace(os.sep, '/').split('/')
        low = [p.lower() for p in parts]
        if 'textures' in low:
            sub = '/'.join(parts[low.index('textures') + 1:]).lower()
            out |= {(sub + '/' if sub else '') + os.path.splitext(f)[0].lower() for f in fn}
    return out


def member_loadfolders(pack_dir, folder):
    """the pack LoadFolders.xml lines for Members/<folder>, prefix stripped, as a v1.6 LoadFolders.xml"""
    lf = os.path.join(pack_dir, 'LoadFolders.xml')
    root = ET.parse(lf).getroot()
    ent = max(root, key=lambda e: tuple(int(t) for t in e.tag.lstrip('v').split('.')))
    pre = 'Members/' + folder
    out = ET.Element('loadFolders'); v = ET.SubElement(out, ent.tag)
    for li in ent:
        t = (li.text or '').strip().replace('\\', '/')
        if t == pre or t.startswith(pre + '/'):
            n = ET.SubElement(v, 'li', li.attrib); n.text = t[len(pre):].lstrip('/') or '/'
    ET.indent(out)
    return out, [li.text for li in v]


def flatten(src, out, bundlex):
    rel = json.load(open(os.path.join(src, 'release.json'), encoding='utf-8'))
    smods, omods = os.path.join(src, 'Mods'), os.path.join(out, 'Mods')
    os.makedirs(omods); log = []
    for e in rel['entries']:
        if e['kind'] == 'keep':
            pid = e['packageId'].lower()
            if pid in DROP: log.append(f"drop  {e['folder']}  {pid}: {DROP[pid]}"); continue
            ws = os.path.join(WS, e['folder'])
            if e['folder'].isdigit() and os.path.isdir(ws): copy_slim(ws, os.path.join(omods, e['folder'])); log.append(f"ws    {e['folder']}  {pid}")
            else: shutil.copytree(os.path.join(smods, e['folder']), os.path.join(omods, e['folder'])); log.append(f"keep  {e['folder']}  {pid}")
            continue
        pack = os.path.join(smods, e['folder'])
        man = json.load(open(os.path.join(pack, 'Pack', 'manifest.json'), encoding='utf-8'))
        provided, local = set(), []
        for m in man['members']:
            pid = m['packageId'].lower(); folder = m['folder'].split('/', 1)[1]
            if pid in DROP: log.append(f"drop  {folder}  {pid}: {DROP[pid]}"); continue
            dst = os.path.join(omods, folder)
            assert not os.path.exists(dst), 'folder name clash ' + folder
            kind, ref = m['source'].split(':', 1)
            if kind == 'workshop':
                copy_slim(os.path.join(WS, ref), dst); provided |= tex_keys(dst); log.append(f"ws    {folder}  {pid}  (from {e['folder']})")
            else:
                shutil.copytree(os.path.join(pack, m['folder']), dst)
                if not os.path.isfile(os.path.join(dst, 'LoadFolders.xml')):
                    lfx, dirs = member_loadfolders(pack, folder)
                    ET.ElementTree(lfx).write(os.path.join(dst, 'LoadFolders.xml'), encoding='utf-8', xml_declaration=True)
                local.append((pid, folder, dst)); log.append(f"local {folder}  {pid}  (from {e['folder']})")
        # bundled textures nobody else provides go back into the local members that reference them
        bi = os.path.join(pack, 'Pack', 'bundle-index.tsv')
        if not os.path.isfile(bi) or not local: continue
        rows = [l.rstrip('\n').split('\t') for l in open(bi, encoding='utf-8') if l.strip() and not l.startswith('#')]
        need = [r for r in rows if r[3].lower() not in provided]
        if not need: continue
        tmp = tempfile.mkdtemp(prefix='hbx_')
        names = os.path.join(tmp, 'names.txt'); open(names, 'w', encoding='utf-8').write('\n'.join(r[2] for r in need))
        r = subprocess.run([bundlex, os.path.join(pack, 'Pack', 'textures'), tmp, names], capture_output=True, text=True)
        log.append(f"tex   {e['folder']}: {r.stdout.strip()} for {len(need)} textures not in workshop copies")
        texts = {}
        for pid, folder, dst in local:
            buf = []
            for dp, dn, fn in os.walk(dst):
                for f in fn:
                    if f.lower().endswith(('.xml', '.dll')): buf.append(open(os.path.join(dp, f), 'rb').read().lower())
            texts[folder] = b'\n'.join(buf)
        for key, idx, asset, orig in need:
            png = os.path.join(tmp, os.path.splitext(asset)[0] + '.png')
            if not os.path.isfile(png): log.append(f"  MISSING decoded {asset}"); continue
            # owner = local member whose XML/DLL mentions the path (or its Graphic_Multi/Random parent); else all
            probes = {orig.lower(), re.sub(r'_(north|south|east|west|m|f|a|b|c)$', '', orig.lower()), orig.lower().rsplit('/', 1)[0]}
            owners = [(f, d) for p, f, d in local if any(x.encode() in texts[f] for x in probes if x)] or [(f, d) for p, f, d in local]
            for f, d in owners:
                t = os.path.join(d, 'Textures', orig + '.png')
                os.makedirs(os.path.dirname(t), exist_ok=True); shutil.copyfile(png, t)
        shutil.rmtree(tmp)
    open(os.path.join(out, 'BUILD-LOG.txt'), 'w', encoding='utf-8').write('\n'.join(log) + '\n')
    print('\n'.join(l for l in log if not l.startswith(('ws ', 'local', 'keep'))))
    print('folders:', len(os.listdir(omods)))


def sha_file(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''): h.update(b)
    return h.hexdigest()


def tree(dirpath):
    """files, bytes, treeHash, dlls exactly like the installer's Util.Measure/TreeHash: a .dds next to a
    same-name .png/.jpg/.jpeg/.psd is skipped, lines sorted by string.CompareOrdinal (UTF-16 code units)"""
    lines, files, size, dlls = [], 0, 0, {}
    for dp, dn, fn in os.walk(dirpath):
        low = {f.lower() for f in fn}
        for f in fn:
            p = os.path.join(dp, f); rel = os.path.relpath(p, dirpath).replace(os.sep, '/'); s = sha_file(p)
            if f.lower().endswith('.dll'): dlls[rel] = s
            stem, ext = os.path.splitext(f)
            if ext.lower() == '.dds' and any((stem + e).lower() in low for e in ('.png', '.jpg', '.jpeg', '.psd')): continue
            lines.append((rel, s)); files += 1; size += os.path.getsize(p)
    lines.sort(key=lambda x: x[0].encode('utf-16-be'))
    th = hashlib.sha256(''.join(f'{r}\t{s}\n' for r, s in lines).encode('utf-8')).hexdigest()
    return files, size, th, dict(sorted(dlls.items()))


def release(src, out, gamebuild):
    old = json.load(open(os.path.join(src, 'release.json'), encoding='utf-8'))
    omods = os.path.join(out, 'Mods')
    # load order: Pack Core's LoadOrder.txt minus dropped mods
    order = [l.strip() for l in open(os.path.join(src, 'Mods', 'HomebrewPackCore', 'LoadOrder.txt'), encoding='utf-8-sig')
             if l.strip() and not l.startswith('#') and l.strip().lower() not in DROP]
    folders = {}
    for f in os.listdir(omods):
        a = os.path.join(omods, f, 'About', 'About.xml')
        r = ET.parse(a).getroot()
        folders[(r.findtext('packageId') or '').strip().lower()] = (f, (r.findtext('name') or f).strip())
    dlc = [x for x in order if x.lower().startswith('ludeon.')]
    missing = [x for x in order if x.lower() not in folders and x not in dlc]
    assert not missing, 'in load order but not built: ' + ', '.join(missing)
    extra = [p for p in folders if p not in {x.lower() for x in order}]
    assert not extra, 'built but not in load order: ' + ', '.join(extra)
    # ModsConfig.xml
    cfg = ET.parse(os.path.join(src, 'Config', 'ModsConfig.xml')).getroot()
    cfg.find('version').text = gamebuild
    am = cfg.find('activeMods'); [am.remove(x) for x in list(am)]
    for x in order: ET.SubElement(am, 'li').text = x.lower()
    ET.indent(cfg)
    ocfg = os.path.join(out, 'Config'); os.makedirs(ocfg, exist_ok=True)
    ET.ElementTree(cfg).write(os.path.join(ocfg, 'ModsConfig.xml'), encoding='utf-8', xml_declaration=True)
    # settings: keep those whose folder still exists
    kept = set(os.listdir(omods)); dropped_cfg = []
    for f in os.listdir(os.path.join(src, 'Config')):
        if f == 'ModsConfig.xml': continue
        m = re.match(r'Mod_(.+?)_[^_]+\.xml$', f)
        owner = next((k for k in kept if f.startswith('Mod_' + k + '_')), None)
        if owner: shutil.copy2(os.path.join(src, 'Config', f), os.path.join(ocfg, f))
        else: dropped_cfg.append(f)
    # release.json: one 'keep' entry per folder, in load order
    entries = []
    for x in order:
        if x in dlc: continue
        f, name = folders[x.lower()]
        files, size, th, dlls = tree(os.path.join(omods, f))
        entries.append({'name': name, 'kind': 'keep', 'folder': f, 'packageId': x.lower(), 'packageIds': [x.lower()],
                        'memberFolders': {}, 'files': files, 'bytes': size, 'treeHash': th, 'dlls': dlls})
    new = dict(old)
    new['gameVersion'] = '1.7'
    new['entries'] = entries
    new['config'] = {f: sha_file(os.path.join(ocfg, f)) for f in sorted(os.listdir(ocfg), key=lambda s: (s != 'ModsConfig.xml', s))}
    new['files'] = sum(e['files'] for e in entries); new['bytes'] = sum(e['bytes'] for e in entries)
    json.dump(new, open(os.path.join(out, 'release.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print(f"entries={len(entries)} active={len(order)} files={new['files']} bytes={new['bytes']} settings_dropped={dropped_cfg}")


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'flatten': flatten(*sys.argv[2:5])
    elif cmd == 'release': release(*sys.argv[2:5])
    else: sys.exit(__doc__)
