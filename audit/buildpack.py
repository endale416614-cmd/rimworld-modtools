import json,sys,os,re,shutil,datetime,xml.etree.ElementTree as ET
mods_json,loadorder,out=sys.argv[1:4]
M=json.load(open(mods_json,encoding='utf-8'))['mods']
VERSION='1.7'; NAME='LocalPack'
REMOVE={ # packageId -> reason (from Player.log 2026-10-17 19:31 launch)
 'dev.soeur.imageopt':'Image Opt: native loader throws AccessViolationException on every texture (2040x), cascades into ~15000 missing-texture/MatFrom errors. 1.6-only.',
 'zetrith.prepatcher':'Prepatcher: no 1.7 build; ships Harmony 2.4.2 in root Assemblies and loads first, shadowing Harmony 3.0 (log: "Harmony Mod v3.0.1.0; Harmony library v2.4.2.0"). Breaks DragSelect (HarmonyInfix TypeLoadException).',
 'telardo.multifloors':'MultiFloors: hard dependency on Prepatcher.',
 'v1024.visibleerrorlogs':'Visible Error Logs: Harmony patch on Verse.Log.Error emits invalid IL on CoreCLR (InvalidProgramException: stack underflow). Needs recompile.',
 'garethp.familytree':'Family Tree: MissingMethodException Verse.LongEventHandler.QueueLongEvent signature changed in 1.7. Needs recompile.',
 'ferny.nomodlistonloading':'No Modlist on Loading: 1.6-only loading-screen patch; prime suspect for 392x DivideByZeroException in GameplayTipWindow.DrawContents. Remove, retest.',
}
PREFER_FOLDER={'owlchemist.toggleableoverlays':'3261316725'}  # two installs; keep the 1.3-1.6 one
SKIP_FILES={('mrk.spellbook','Assets.zip')}  # 244-byte damaged asset pack, game ignores it
verdir=re.compile(r'^v?\d+\.\d+$')

# parse LoadOrder.proposed.txt -> ordered (pack, pid)
order=[]; pack=None; titles={}
for l in open(loadorder,encoding='utf-8'):
    l=l.rstrip('\n')
    m=re.match(r'# == (\S+) (.*)',l)
    if m: pack=m.group(1); titles[pack]=m.group(2); continue
    if l and not l.startswith('#'): order.append((pack,l.strip()))
order=[(p,a) for p,a in order if a not in REMOVE]

def keep_version_dirs(src):
    vers=[x for x in os.listdir(src) if verdir.match(x) and os.path.isdir(os.path.join(src,x))]
    keep=set()
    if vers:
        nums=sorted(vers,key=lambda v:tuple(int(t) for t in v.lstrip('v').split('.')))
        best=[v for v in nums if v.lstrip('v') in('1.7','1.6')]
        keep={best[-1]} if best else {nums[-1]}
    lf=os.path.join(src,'LoadFolders.xml')
    if os.path.isfile(lf):
        try:
            root=ET.parse(lf).getroot(); ents={}
            for e in root:
                v=e.tag.lstrip('v')
                try: ents[tuple(int(t) for t in v.split('.'))]=[(c.text or '').strip() for c in e]
                except Exception: pass
            cands=[k for k in ents if k<=(1,7)]
            if cands:
                for f in ents[max(cands)]:
                    top=f.replace('\\','/').strip('/').split('/')[0]
                    if top and top!='.': keep.add(top)
        except Exception: pass
    return keep,vers

def copy_mod(pid,src,dst):
    keep,vers=keep_version_dirs(src); n=0; b=0
    for dp,dn,fn in os.walk(src):
        rel=os.path.relpath(dp,src); top=rel.split(os.sep)[0]
        if top in ('Source','.git') or (top in vers and top not in keep): dn[:]=[]; continue
        for f in fn:
            if (pid,f) in SKIP_FILES and rel=='.': continue
            s=os.path.join(dp,f); d=os.path.join(dst,rel,f)
            os.makedirs(os.path.dirname(d),exist_ok=True); shutil.copy2(s,d); n+=1; b+=os.path.getsize(s)
    return n,b,sorted(keep)

os.makedirs(os.path.join(out,'Mods'),exist_ok=True)
entries=[]; used=set()
for p,a in order:
    if a.startswith('ludeon.'): entries.append(dict(packageId=a,pack=p,kind='dlc')); continue
    m=M[a]; src=m['dir']
    if a in PREFER_FOLDER: src=os.path.join(os.path.dirname(src),PREFER_FOLDER[a])
    folder=f"{p}__{a}"; dst=os.path.join(out,'Mods',folder)
    if os.path.isdir(dst): shutil.rmtree(dst)
    n,b,kept=copy_mod(a,src,dst)
    entries.append(dict(packageId=a,name=m['name'],pack=p,kind='mod',folder=folder,workshopId=m['wid'],source=src,supportedVersions=[v for v in m['vers'] if v],has17=m['has17'],hasAssemblies=m['hasAssemblies'],files=n,bytes=b,versionFoldersKept=kept))
    print(f'{len(entries):4d} {folder} {n} files {b/2**20:.1f} MiB',flush=True)

# verify every copied About.xml parses and packageId matches
bad=[]
for e in entries:
    if e['kind']!='mod': continue
    p=os.path.join(out,'Mods',e['folder'],'About','About.xml')
    try:
        pid=(ET.parse(p).getroot().findtext('packageId') or '').strip().lower()
        if pid!=e['packageId']: bad.append((e['folder'],pid))
    except Exception as ex: bad.append((e['folder'],str(ex)))
assert not bad,bad

active=[a for p,a in order]
xml='<?xml version="1.0" encoding="utf-8"?>\n<ModsConfigData>\n  <version>1.7.4981 rev136</version>\n  <activeMods>\n'+''.join(f'    <li>{a}</li>\n' for a in active)+'  </activeMods>\n  <knownExpansions>\n'+''.join(f'    <li>ludeon.rimworld.{d}</li>\n' for d in ['royalty','ideology','biotech','anomaly','odyssey'])+'  </knownExpansions>\n</ModsConfigData>\n'
open(os.path.join(out,'ModsConfig.xml'),'w',encoding='utf-8').write(xml)
lo='# LocalPack v1.7 load order. One packageId per line; pack headers are comments.\n'
last=None
for p,a in order:
    if p!=last: lo+=f'\n# == {p} {titles.get(p,p)}\n'; last=p
    lo+=a+'\n'
open(os.path.join(out,'LoadOrder.txt'),'w',encoding='utf-8').write(lo)
rel=dict(name=NAME,version=VERSION,gameVersion='1.7.4981 rev136',built=datetime.datetime.now().isoformat(timespec='seconds'),
 dlcs=[a for a in active if a.startswith('ludeon.')],packs=[dict(key=k,title=t,count=sum(1 for e in entries if e['pack']==k)) for k,t in titles.items()],
 removed=[dict(packageId=k,name=M[k]['name'] if k in M else k,reason=v) for k,v in REMOVE.items()],entries=entries)
json.dump(rel,open(os.path.join(out,'release.json'),'w',encoding='utf-8'),indent=1,ensure_ascii=False)
tot=sum(e.get('bytes',0) for e in entries)
print('DONE mods',sum(1 for e in entries if e['kind']=='mod'),'total',f'{tot/2**30:.2f} GiB','removed',len(REMOVE))
