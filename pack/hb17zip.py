"""Zip a built Homebrew tree into the installer's data zip: release.json, Config/*, Mods/** (no directory
entries, forward slashes, deflate), then re-open it and check every release.json entry against the zip.
  py -I hb17zip.py BUILD OUT.zip"""
import hashlib, json, os, sys, zipfile

build, out = sys.argv[1:3]
rel = json.load(open(os.path.join(build, 'release.json'), encoding='utf-8'))
with zipfile.ZipFile(out + '.tmp', 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    z.write(os.path.join(build, 'release.json'), 'release.json')
    for top in ('Config', 'Mods'):
        for dp, dn, fn in os.walk(os.path.join(build, top)):
            dn.sort()
            for f in sorted(fn):
                p = os.path.join(dp, f)
                z.write(p, os.path.relpath(p, build).replace(os.sep, '/'))
# verify: every config hash and every DLL hash in release.json matches the zipped bytes
with zipfile.ZipFile(out + '.tmp') as z:
    names = set(z.namelist())
    for f, h in rel['config'].items():
        assert hashlib.sha256(z.read('Config/' + f)).hexdigest() == h, f
    for e in rel['entries']:
        for r, h in e['dlls'].items():
            assert hashlib.sha256(z.read(f"Mods/{e['folder']}/{r}")).hexdigest() == h, (e['folder'], r)
    n = sum(1 for x in names if x.startswith('Mods/'))
    assert n >= rel['files'], (n, rel['files'])  # >=: .dds twins are zipped but not counted
os.replace(out + '.tmp', out)
print(f"{out}: {len(names)} entries, {os.path.getsize(out) / 2**30:.2f} GiB")
