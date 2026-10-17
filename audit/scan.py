import os,sys,json,re,xml.etree.ElementTree as ET
roots=[r"F:\Steam\steamapps\workshop\content\294100",r"F:\Steam\steamapps\common\RimWorld\Mods",r"F:\Steam\steamapps\common\RimWorld\Data"]
active=[l.strip() for l in open(sys.argv[1],encoding='utf-8') if l.strip()]
idx={}
def txt(e,t):
    x=e.find(t); return (x.text or '').strip() if x is not None and x.text else ''
def lst(e,t):
    x=e.find(t); return [(c.text or "").strip() for c in x] if x is not None else []
for root in roots:
    if not os.path.isdir(root): continue
    for d in os.listdir(root):
        p=os.path.join(root,d,'About','About.xml')
        if not os.path.isfile(p): continue
        try: e=ET.parse(p).getroot()
        except Exception as ex: print('BAD',p,ex,file=sys.stderr); continue
        pid=txt(e,'packageId').lower()
        if not pid: continue
        top=set(os.listdir(os.path.join(root,d)))
        vers=[(c.text or "") for c in e.find("supportedVersions")] if e.find('supportedVersions') is not None else []
        deps=lst(e,'modDependencies'); deps=[]
        md=e.find('modDependencies')
        if md is not None: deps=[txt(c,'packageId').lower() for c in md]
        mdv=e.find('modDependenciesByVersion')
        if mdv is not None:
            for v in mdv:
                deps+= [txt(c,'packageId').lower() for c in v]
        incompat=[(c.text or "").strip().lower() for c in e.find('incompatibleWith')] if e.find('incompatibleWith') is not None else []
        iv=e.find('incompatibleWithByVersion')
        if iv is not None:
            for v in iv: incompat+=[(c.text or "").strip().lower() for c in v]
        la=[(c.text or "").strip().lower() for c in e.find('loadAfter')] if e.find('loadAfter') is not None else []
        lb=[(c.text or "").strip().lower() for c in e.find('loadBefore')] if e.find('loadBefore') is not None else []
        pub=txt(e,'steamAppId') or ''
        pf=os.path.join(root,d,'About','PublishedFileId.txt')
        wid=open(pf).read().strip() if os.path.isfile(pf) else (d if d.isdigit() else '')
        has17=any(v.strip().startswith('1.7') for v in vers)
        vfolders=[x for x in top if re.match(r'^1\.\d',x)]
        rec=dict(pid=pid,name=txt(e,'name'),author=txt(e,'author') or ','.join(lst(e,'authors')),vers=sorted(vers),deps=deps,incompat=incompat,loadAfter=la,loadBefore=lb,wid=wid,dir=os.path.join(root,d),has17=has17,vfolders=sorted(vfolders),hasAssemblies='Assemblies' in top or any(os.path.isdir(os.path.join(root,d,v,'Assemblies')) for v in vfolders),desc=txt(e,'description')[:300].replace('\n',' '))
        if pid in idx and idx[pid]['wid']!=wid: rec['dupInstall']=idx[pid]['dir']
        idx[pid]=rec
out={'active':active,'mods':idx}
json.dump(out,open(sys.argv[2],'w',encoding='utf-8'),indent=1)
missing=[a for a in active if a.lower() not in idx]
print('scanned',len(idx),'active',len(active),'missing',len(missing))
for m in missing: print('MISSING',m)
no17=[a for a in active if a.lower() in idx and not idx[a.lower()]['has17']]
print('active not 1.7:',len(no17))
