import json,sys,os,re
d=json.load(open(sys.argv[1],encoding='utf-8')); M=d['mods']; act=[a.lower() for a in d['active']]
pats=[b'Dialog_Rename',b'DebugMenuOption',b'UnityEngine.UI',b'System.Drawing',b'Multiplayer']
# removed-in-1.7 types list come from Polarisbloc failure; extend via ModUpdating later
out=[]
for a in act:
    m=M[a]
    if not m['hasAssemblies']: continue
    root=m['dir']; dlls=[]
    for dp,dn,fn in os.walk(root):
        if os.path.basename(dp).lower()=='assemblies':
            # choose only version folders relevant: top-level, 1.6, 1.7, Current, Common
            rel=os.path.relpath(dp,root).replace(os.sep,'/')
            if re.match(r'^(1\.[0-5]|v?1\.[0-5]|1\.[0-5]/.*)',rel): continue
            for f in fn:
                if f.lower().endswith('.dll') and not f.startswith('0Harmony'): dlls.append(os.path.join(dp,f))
    hits=set()
    for f in dlls:
        try: b=open(f,'rb').read()
        except: continue
        for p in pats:
            if p in b: hits.add(p.decode())
    if hits: out.append((a,m['name'],sorted(hits)))
for a,n,h in out: print(f"{a} | {n[:40]} | {','.join(h)}")
print(len(out),'mods hit')
