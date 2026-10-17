import json,sys,os,heapq
d=json.load(open(sys.argv[1],encoding='utf-8')); M=d['mods']
P=json.load(open(sys.argv[2],encoding='utf-8')); assign=P['assign']; order=P['order']; titles=P['titles']
OUT=sys.argv[3]; os.makedirs(OUT,exist_ok=True)
N=lambda a:M[a]['name'] or a
W=lambda a:(' (workshop '+M[a]['wid']+')') if M[a].get('wid','').isdigit() else ''

# --- A. hard removes: max supported version < 1.6 (cannot load on 1.7 engine) or logged type-load failure
def maxver(a):
    vs=[v for v in M[a]['vers'] if v and v[0].isdigit()]
    return max(vs,key=lambda v:tuple(int(x) for x in v.split('.')[:2])) if vs else '0'
hard=[a for a in order if a!='ludeon.rimworld' and tuple(int(x) for x in maxver(a).split('.')[:2])<(1,6)]
hard_reason={a:f'supports up to {maxver(a)} only' for a in hard}
hard_reason['vanya.polarisbloc.corelab']='Player.log: ReflectionTypeLoadException (Verse.Dialog_Rename / Verse.DebugMenuOption missing); 1.4-era DLL'
hard_reason.setdefault('vanya.polarisbloc.securityforce','1.4-era DLL, depends on Polarisbloc Core LAB')
hard=[a for a in order if a in hard_reason]

# --- B. config / install problems
config=[
 ('DLC entries duplicated at the end of ModsConfig.xml (positions 814-818: Royalty, Ideology, Biotech, Anomaly, Odyssey). Every framework then "loads before" its DLC dependency. Proposed list keeps the first copy only.'),
 ('Toggleable Overlays installed twice: workshop 3261316725 (1.3-1.6) and 2608654598 (1.3-1.4). Game logged "Tried loading mod with the same packageId multiple times". Unsubscribe 2608654598.'),
 ('Spellbook (workshop 3727983758): Assets.zip is 244 bytes, game logged "could not be opened ... manifest is damaged". Unsubscribe, let Steam re-download, verify.'),
 ('Replace Stuff Mod Compatibility (garethp) requires uuugggg.replacestuff which is not installed; active list has memegoddess.replacestuff (Continued) instead. Drop the compat mod or it does nothing.'),
 ('Eccentric Tech - Cosmetic Modifications requires Humanoid Alien Races (installed, workshop 1578693166, inactive). Activate HAR or drop the mod.'),
 ('MultiFloors requires Prepatcher (installed, workshop 2934420800, inactive). Activate Prepatcher above Harmony or drop MultiFloors. Homebrew pack also puts Prepatcher first.'),
]

# --- C. declared incompatibles (both active)
aset=set(order); incompat=[]
for a in order:
    for x in M[a]['incompat']:
        if x in aset and (x,a) not in incompat: incompat.append((a,x))

# --- D. functional duplicates / overlaps (manual; one line each)
dups=[
 ('Hospitality (Continued)','Hotels and Recreation','declared incompatible; both run guest/lodging systems. Keep one.'),
 ('[FSF] Vanilla Bionics Expansion','[FSF] Advanced Bionics Expansion','declared incompatible by both. Keep one.'),
 ('RimWorld Together','RimSaves','declared incompatible. Keep RimWorld Together only while playing multiplayer.'),
 ('Smart Odyssey','Alpha Biomes','declared incompatible by Smart Odyssey.'),
 ('Change Style Anytime (cedaro.csa)','Style Change Anytime (dra.stylechangeanytime)','same feature, two mods. Keep one.'),
 ('Dubs Mint Menus','Better Architect Menu (+Tweaks), Basic Dropdowns, Float Sub-Menus, Material Sub-Menu, Architect Icons (+Improved)','four mods replace or restyle the architect menu. Dubs Mint Menus and Better Architect Menu both replace it outright; pick one and drop the submenu/icon mods it makes redundant.'),
 ('RimHUD','Vivid Rim - Needs Tab, Pawn quick info, Info Card Plus','RimHUD already replaces the inspect pane and needs tab. Keep RimHUD, drop Vivid Rim Needs Tab.'),
 ('Nice Health Tab','Compact Hediffs','both redraw the health tab; known to fight. Keep one.'),
 ('Nice Bill Tab (+Expansion)','Better Workbench Management','both patch the bill tab UI. Keep one.'),
 ('Compositable Loadouts','Dress Code, Choose Your Outfit, Dress for the Weather','Compositable Loadouts replaces the outfit system; the other three patch the vanilla one. Keep Compositable Loadouts alone or the other three.'),
 ('Melee Animation (+Vanilla)','Yayo\'s Animation (Continued)','both animate pawn combat; Melee Animation overrides Yayo for melee. Works but redundant; drop Yayo if Melee Animation is kept.'),
 ('Blood Animations','Gore Perfected','overlapping gore/blood effects. Keep one.'),
 ('[FM] Gene Banks Expanded','Oops All Gene Banks','both change gene bank capacity/behaviour. Keep one.'),
 ('Quality Bionics Remastered','Elite Bionics Framework, Bionic modularity','three bionic-quality/upgrade systems. Keep one system.'),
 ('[sbz] Fridge','Simple Utilities: Fridge (1.4 only)','same function; the Owlchemist one is on the remove list anyway.'),
 ('Doors Expanded','Simple Doors, ReBuild: Doors and Corners, Seamless Doors','three door-type mods plus a visual one. Doors Expanded + ReBuild overlap most; keep one of the two.'),
 ('Seamless Embrasures','Rebuild: Embrasures, VFE Security','three embrasure sources. Keep one.'),
 ('Vanilla Textures Expanded','More Vanilla Textures, Clean Textures, AI Upscaled Textures Core','four vanilla retexture packs; last loaded wins, the rest only cost load time and VRAM. Keep VTE (1.7) and drop the other three.'),
 ('Vanilla Trading Expanded','Trading House, RimMarket, Auto Seller, Call A Trader, Settlement Services','overlapping trade systems; Trading House and RimMarket both add player shops. Keep one shop mod.'),
 ('Zoology: Realistic Animal Overhaul','Animal Traits System, Animals Logic','Zoology overhauls animal stats/behaviour and will fight Animal Traits System. Keep one.'),
 ('Vanilla Nutrient Paste Expanded','Replimat','both are automated food dispensing. Compatible, but redundant.'),
 ('Won hair_men / hair_women','Won Hair Retextured (men/women)','retexture add-ons: fine, but the add-on is 1.6-only.'),
 ('Doors Expanded / Misc. Training / QualityBuilder / Repair Workbench','[sbz] retextures for each','add-ons, fine; listed so you know they go after their parent.'),
]

# --- E. untested on 1.7 (no 1.7 tag, ships a DLL). Engine is Waylith (.NET 10 / Vulkan), so these are real risk.
untested=[a for a in order if a not in hard and not M[a]['has17'] and M[a]['hasAssemblies']]
untested_xml=[a for a in order if a not in hard and not M[a]['has17'] and not M[a]['hasAssemblies'] and a!='ludeon.rimworld']
highrisk={'telefonmast.graphicssettings':'hooks Unity texture pipeline; engine is no longer Unity',
 'airetexture.core':'Unity texture-memory hacks',
 'depscian.rimtunes':'Unity AudioSource streaming','swablu.ambience':'Unity audio','ancestral.narration':'Unity audio',
 'v1024.visibleerrorlogs':'patches Unity Debug log handler','taveron.increasedstack':'reflection on stack limits','nova.rimworldtogether':'network/multiplayer, 1.6 only',
 'co.uk.epicguru.meleeanimation':'heavy Harmony transpilers on rendering','com.yayo.yayoani.continued':'heavy rendering patches','nals.facialanimation':'rendering patches',
 'smashphil.vehicleframework':'large framework, 1.6 only; 10 vehicle mods depend on it','unlimitedhugs.hugslib':'framework, 1.6 only; many mods depend on it','adaptive.storage.framework':'framework, 1.6 only',
 'ebsg.framework':'framework, 1.6 only','imranfish.xmlextensions':'framework, 1.6 only','owlchemist.cherrypicker':'def-removal at load; 1.6 only','taranchuk.fastergameloading':'load-pipeline patches'}

# --- F. proposed order: pack order, then topo-fix loadAfter/loadBefore/deps with minimal movement
packkeys=sorted(titles)
alive=[a for a in order if a not in hard]; bset=set(alive)
front=['brrainz.harmony','chezhou.rimunity','v1024.visibleerrorlogs','taranchuk.fastergameloading','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey']
after={a:set() for a in alive}  # after[a] = mods that must load before a
for a in alive:
    for x in M[a]['loadAfter']+M[a]['deps']:
        if x in bset and x!=a and not (a=='keshash.layeredwalldestruction' and x=='seromas.layereddestruction.patches'): after[a].add(x)  # patch mod says loadAfter parent; parent's dep on it is a cycle
    for x in M[a]['loadBefore']:
        if x in bset and x!=a: after[x].add(a)
# lift a mod into the latest pack of anything it must load after, so packs stay contiguous
pk={a:assign[a] for a in alive}; lifted={}
changed=True
while changed:
    changed=False
    for a in alive:
        if a in front: continue
        deps=[x for x in after[a] if x not in front]
        if not deps: continue
        mx=max(pk[x] for x in deps)
        if mx>pk[a]:
            lifted[a]=(pk[a],mx,max(deps,key=lambda x:pk[x])); pk[a]=mx; changed=True
base=[a for k in packkeys for a in alive if pk[a]==k]
pri={a:i for i,a in enumerate(base)}
for i,a in enumerate(front):
    if a in pri: pri[a]=-100+i
for a in alive:
    if a.startswith('ludeon.rimworld.'): after[a].add('ludeon.rimworld')
    elif a!='ludeon.rimworld' and a not in front: after[a].add('ludeon.rimworld.odyssey')
indeg={a:len(after[a]) for a in base}; succ={a:[] for a in base}
for a in base:
    for x in after[a]: succ[x].append(a)
heap=[(pri[a],a) for a in base if indeg[a]==0]; heapq.heapify(heap); final=[]
while heap:
    _,a=heapq.heappop(heap); final.append(a)
    for s in succ[a]:
        indeg[s]-=1
        if indeg[s]==0: heapq.heappush(heap,(pri[s],s))
assert len(final)==len(base), 'cycle in load constraints'
assign=pk
open(os.path.join(OUT,'LoadOrder.proposed.txt'),'w',encoding='utf-8').write('# Proposed load order, one packageId per line. Pack headers are comments.\n'+''.join((f'\n# == {k} {titles[k]}\n' if i==0 or assign[final[i-1]]!=assign[a] else '')+a+'\n' for i,a in enumerate(final) for k in [assign[a]]))
xml='<?xml version="1.0" encoding="utf-8"?>\n<ModsConfigData>\n  <version>1.7.4981 rev136</version>\n  <activeMods>\n'+''.join(f'    <li>{M[a]["pid_raw"] if "pid_raw" in M[a] else a}</li>\n' for a in final)+'  </activeMods>\n  <knownExpansions>\n'+''.join(f'    <li>{a}</li>\n' for a in front[5:])+'  </knownExpansions>\n</ModsConfigData>\n'
open(os.path.join(OUT,'ModsConfig.proposed.xml'),'w',encoding='utf-8').write(xml)

# --- report
L=[]; w=L.append
w('# RimWorld 1.7 mod list audit (2026-10-17)\n')
w('Source: ModsConfig.xml (818 entries, 812 unique), workshop folder 294100 (1219 items), Player.log from the 18:23 launch.\n')
w('Game is RimWorld 1.7.4981 on the new Waylith engine (.NET 10, Vulkan, no Unity). 479 of 812 active mods have no 1.7 tag; 294 of those ship a DLL compiled for 1.6 or older.\n')
w('## 1. Why the launch hung\n')
w('Player.log shows no crash. Timeline:\n')
w('- 18:24:51 game init. 18:25:16 mod list built. 18:25:23 duplicate packageId error (Toggleable Overlays). 18:25:29 Spellbook Assets.zip damaged.')
w('- 18:31:56 `ReflectionTypeLoadException getting types in assembly Polarisbloc` (Verse.Dialog_Rename and Verse.DebugMenuOption no longer exist in 1.7). This is during `ModAssemblyHandler.ReloadAll`, i.e. the game was still loading mod DLLs 7 minutes after start.')
w('- Nothing logged after that until 18:37:35 "Rendering resumed after 592.7 s minimized", which is you killing it. The game was minimised for 10 minutes while still in assembly loading.\n')
w('So: not a hard crash, the assembly-load stage with 300+ 1.6-era DLLs on the new runtime is extremely slow and at least one DLL (Polarisbloc) fails outright. Position 334 of 812 was reached at 18:31; the remainder never logged. Fix the hard failures below, cut the untested-DLL count, then retest. If it still hangs, bisect by pack (disable half the packs at a time) using LoadOrder.proposed.txt.\n')
w('## 2. Must remove (cannot load on 1.7)\n')
for a in hard: w(f'- {N(a)} (`{a}`){W(a)}: {hard_reason[a]}')
w('\n## 3. Config and install problems\n')
for c in config: w(f'- {c}')
w('\n## 4. Declared incompatible, both active\n')
for a,x in incompat: w(f'- {N(a)} (`{a}`)  <->  {N(x)} (`{x}`)')
w('\n## 5. Duplicate function / overlap (pick one)\n')
for a,b,why in dups: w(f'- **{a}** vs {b}: {why}')
w('\n## 6. Load-order violations in the current list\n')
w('- pesky.ae (Pesky\'s Archotech Exosuit) loads before its dependency zal.morearchotechgarbage.')
w('- keshash.layeredwalldestruction lists seromas.layereddestruction.patches as a dependency while that patch mod says loadAfter the parent: circular. Ignored the parent side; patch loads after parent.')
w('- ilarion.bulkloadfortransporters must load after mehni.pickupandhaul.')
w('- Every framework "loads before" the DLCs because of the duplicate DLC entries (section 3).')
w('- All four fixed in LoadOrder.proposed.txt.\n')
w(f'## 7. No 1.7 tag + ships a DLL ({len(untested)} mods): test, update or drop\n')
w('Highest risk first (engine-level hooks), then the rest alphabetically.\n')
for a in untested:
    if a in highrisk: w(f'- **{N(a)}** (`{a}`){W(a)}: {highrisk[a]}')
w('')
for a in sorted(untested,key=N):
    if a not in highrisk: w(f'- {N(a)} (`{a}`) {",".join(M[a]["vers"])}')
w(f'\n## 8. No 1.7 tag, XML/texture only ({len(untested_xml)} mods)\n')
w('Usually load fine; expect yellow "may not work" warnings and odd defs. Not listed one by one; see packs.json.\n')
w('## 9. Packs (Homebrew-style grouping)\n')
w('Order = pack order; inside a pack the current relative order is kept, then dependency constraints applied. Full list in LoadOrder.proposed.txt; same list as ModsConfig.proposed.xml (not installed, copy by hand). Mods from section 2 are already removed; section 4/5 choices are NOT applied, both sides are still in.\n')
for k in packkeys:
    items=[a for a in final if assign[a]==k]
    if not items: continue
    w(f'### {k} {titles[k]} ({len(items)})\n')
    for a in items:
        tag=('' if M[a]['has17'] else (' [no 1.7, DLL]' if M[a]['hasAssemblies'] else ' [no 1.7]'))
        w(f'- {N(a)} (`{a}`){tag}')
    w('')
if lifted:
    w('## 10. Mods moved to a later pack because of a load-after dependency\n')
    for a,(f,t,dep) in sorted(lifted.items(),key=lambda kv:kv[1][1]): w(f'- {N(a)} (`{a}`): {f} -> {t}, must load after {N(dep)}')
open(os.path.join(OUT,'REPORT.md'),'w',encoding='utf-8').write('\n'.join(L)+'\n')
print('hard',len(hard),'untested-dll',len(untested),'untested-xml',len(untested_xml),'final',len(final),'lifted',len(lifted))
