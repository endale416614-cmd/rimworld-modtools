import json,sys,os,re,shutil
pack,live=sys.argv[1],sys.argv[2]
REMOVE={
 'telefonmast.graphicssettings':'Graphics Settings+: replaces the texture loader; on the 1.7 engine every vanilla texture load fails ("[Graphics Settings] Could not load texture", 5,327 errors in run 20261017_215230). 1.6-only.',
 'rh2.faction.void':'[RH2] Faction: V.O.I.D.: HarmonyException patching RimWorld.Faction.TryAffectGoodwillWith ("Unrelated classes cannot describe the same non-null object"); EnlistMod fails to instantiate. 1.6-only.',
 'sov.det.surrogatemech':'Surrogate Mechanoid: transpiler on JobDriver_OperateDeepDrill emits invalid IL (InvalidProgramException: Inconsistent stack depth); mod fails to instantiate. 1.6-only.',
}
def strip_xml(p):
    s=open(p,encoding='utf-8').read()
    for a in REMOVE: s=re.sub(r'[ \t]*<li>'+re.escape(a)+r'</li>\r?\n','',s)
    for a in REMOVE: assert a not in s,a
    open(p,'w',encoding='utf-8').write(s); return len(re.findall(r'<li>',s))
print('live li',strip_xml(live))
print('pack li',strip_xml(os.path.join(pack,'ModsConfig.xml')))
lo=os.path.join(pack,'LoadOrder.txt'); t=open(lo,encoding='utf-8').read()
for a in REMOVE: t=re.sub(r'^'+re.escape(a)+r'\r?\n','',t,flags=re.M)
open(lo,'w',encoding='utf-8').write(t)
rj=os.path.join(pack,'release.json'); r=json.load(open(rj,encoding='utf-8'))
for e in r['entries']:
    if e['packageId'] in REMOVE and e['kind']=='mod':
        d=os.path.join(pack,'Mods',e['folder'])
        if os.path.isdir(d): shutil.rmtree(d); print('deleted',e['folder'])
        r['removed'].append(dict(packageId=e['packageId'],name=e['name'],reason=REMOVE[e['packageId']]))
r['entries']=[e for e in r['entries'] if e['packageId'] not in REMOVE]
for p in r['packs']: p['count']=sum(1 for e in r['entries'] if e['pack']==p['key'])
json.dump(r,open(rj,'w',encoding='utf-8'),indent=1,ensure_ascii=False)
print('entries',len(r['entries']),'removed',len(r['removed']),'folders',len(os.listdir(os.path.join(pack,'Mods'))))
