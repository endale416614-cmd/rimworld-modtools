import json,sys,re
d=json.load(open(sys.argv[1],encoding='utf-8'))
act=[a.lower() for a in d['active']]; M=d['mods']; pos={a:i for i,a in enumerate(act)}
aset=set(act)
print('== ORDER (idx pid | name | v17 asm)')
for i,a in enumerate(act):
    m=M[a]; print(f"{i:3d} {a} | {m['name'][:45]} | {'17' if m['has17'] else '--'}{'A' if m['hasAssemblies'] else '-'} {','.join(v for v in m['vers'] if v)}")
print('\n== DECLARED INCOMPATIBLE (both active)')
for a in act:
    for x in M[a]['incompat']:
        if x in aset: print(f"{a}  <->  {x}")
print('\n== MISSING DEPENDENCY (dep not active)')
for a in act:
    for x in M[a]['deps']:
        if x and x not in aset: print(f"{a}  needs  {x}  {'(installed, inactive)' if x in M else '(not installed)'}")
print('\n== LOAD ORDER VIOLATIONS')
for a in act:
    for x in M[a]['loadAfter']:
        if x in aset and pos[x]>pos[a]: print(f"{a} should load AFTER {x}")
    for x in M[a]['loadBefore']:
        if x in aset and pos[x]<pos[a]: print(f"{a} should load BEFORE {x}")
    for x in M[a]['deps']:
        if x in aset and pos[x]>pos[a]: print(f"{a} dep {x} loads later")
print('\n== DUPLICATE INSTALL (same packageId, two folders)')
for a,m in M.items():
    if m.get('dupInstall'): print(a, m['dir'], m['dupInstall'], 'ACTIVE' if a in aset else '')
