"""Migrate RimWorld mod settings files from workshop folder names to pack folder names.

RimWorld stores mod settings as Config\Mod_<FolderName>_<SettingsClass>.xml. A mod moved
from the workshop folder <id> to a pack folder NN-pack__packageId therefore starts with
default settings and the old file is orphaned. This copies Mod_<id>_X.xml over
Mod_<folder>_X.xml using release.json (workshopId -> folder), keeping the pack-side file
when it was changed in game after the pack install (mtime newer than --keep-after).

usage: py -I migrate-settings.py <release.json> <ConfigDir> [--keep-after "YYYY-MM-DD HH:MM"] [--apply]
"""
import json, os, sys, shutil, datetime, zipfile

rel, cfg = sys.argv[1], sys.argv[2]
apply = '--apply' in sys.argv
keep_after = None
if '--keep-after' in sys.argv:
    keep_after = datetime.datetime.strptime(sys.argv[sys.argv.index('--keep-after') + 1], '%Y-%m-%d %H:%M').timestamp()
# Settings files that are really caches tied to the installed mod set; migrating them replays a stale cache.
SKIP_CLASSES = ('FasterGameLoadingMod',)
entries = [e for e in json.load(open(rel, encoding='utf-8'))['entries'] if e.get('kind') in ('mod', 'keep') and e.get('workshopId', '').isdigit()]
by_id = {e['workshopId']: e['folder'] for e in entries}
files = os.listdir(cfg)
plan = []
for f in files:
    if not (f.startswith('Mod_') and f.endswith('.xml')):
        continue
    rest = f[4:]
    for wid, folder in by_id.items():
        if rest.startswith(wid + '_'):
            cls = rest[len(wid) + 1:]
            if cls[:-4] in SKIP_CLASSES:
                plan.append(('skip-cache', f, f'Mod_{folder}_{cls}')); break
            target = f'Mod_{folder}_{cls}'
            src, dst = os.path.join(cfg, f), os.path.join(cfg, target)
            if os.path.isfile(dst):
                same = open(src, 'rb').read() == open(dst, 'rb').read()
                if same:
                    plan.append(('same', f, target)); break
                if keep_after and os.path.getmtime(dst) > keep_after:
                    plan.append(('keep-pack', f, target)); break
                plan.append(('overwrite', f, target))
            else:
                plan.append(('create', f, target))
            break
counts = {}
for k, _, _ in plan:
    counts[k] = counts.get(k, 0) + 1
print('plan', counts)
for k, f, t in plan:
    if k in ('overwrite', 'keep-pack'):
        print(f'  {k:9} {f} -> {t}')
if not apply:
    print('dry run; add --apply to copy'); sys.exit(0)
stamp = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
backup = os.path.join(cfg, f'ModSettings.backup-{stamp}.zip')
with zipfile.ZipFile(backup, 'w', zipfile.ZIP_DEFLATED) as z:
    for f in files:
        if f.startswith('Mod_') and f.endswith('.xml'):
            z.write(os.path.join(cfg, f), f)
print('backup', backup)
done = 0
for k, f, t in plan:
    if k in ('create', 'overwrite'):
        shutil.copy2(os.path.join(cfg, f), os.path.join(cfg, t)); done += 1
print('copied', done)
