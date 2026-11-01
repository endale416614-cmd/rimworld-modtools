"""Write yc's Faction Editor settings that dress and arm each faction by theme.

Run: py -I factiontheme.py DUMP_PREFIX SETTINGS_XML REPORT_MD
DUMP_PREFIX comes from harness/proberun.ps1 -Mode dump. Only anime style packs dress the visitor kinds (LP17_Visitor*), and
LP17 Hotfix keeps those packs out of all other pawn generation. Factions missing from THEMES keep their own gear.
"""
import collections
import os
import re
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from classify import load, table, tags

FRANCHISE = {
    'fantasy': ['mlmlmlm.cosplayapparelgenshinimpact', 'mihoyo.genshinimpact.characterstyles', 'mlmlmlm.cosplayapparelgranbluefantasy', 'mlmlmlm.cosplayapparelprincessconnect', 'rf.ateliersophie.characterstyles', 'ricofox233.touhoustyle.youyoumu', 'jupiter.madeinabyss', 'ricofox233.mia.whiteflutepack', 'mlmlmlm.cosplayapparelcodevein'],
    'school': ['mlmlmlm.cosplayequipmentbluearchive', 'mlmlmlm.cosplayapparelumamusumeprettyderby', 'rf.bangdream.mygostylepack', 'ricofox233.btr.stylepack', 'ricofox233.yuzu.llljcharacterstyles', 'ricofox.apparel.mgwtpack', 'mlmlmlm.cosplayappareleternalreturn'],
    'scifi': ['mlmlmlm.cosplayapparelhonkaistarrail', 'snowbreak.containmentzone.characterstyles', 'mlmlmlm.cosplayapparelzenlesszonezero', 'mihoyo.zenlesszonezero.characterstyles', 'mlmlmlm.cosplayapparelwutheringwaves', 'kuro.wutheringwaves.characterstyles', 'mlmlmlm.cosplayapparelcounterside'],
    'military': ['ricofox233.nl.gfpack', 'ricofox233.ptwzz.gfpack', 'ricofox233.ptc.gfpack', 'ricofox233.ptcyl.gfpack', 'ricofox233.pnc.gfytpack', 'ricofox233.girlsfrontline.defyteam', 'ricofox233.girlsfrontline.arteam', 'jupiter.exilium', 'mlmlmlm.cosplayequipmentgoddessofvictorynikke', 'mlmlmlm.cosplayapparelazurlane'],
}

TRIBAL = dict(weapons=['neolithic'], melee=['neolithic'], apparel=['neolithic'], anime='fantasy', note='bows, spears, tribalwear, furs')
CLAN = dict(weapons=['neolithic', 'medieval'], melee=['medieval', 'neolithic'], apparel=['medieval', 'neolithic'], anime='fantasy', note='rough medieval arms, hides, coastal and Greekcore wear')
KINGDOM = dict(weapons=['medieval'], melee=['medieval'], apparel=['medieval'], anime='fantasy', note='knights: plate, Greekcore armor, swords, crossbows')
UNION = dict(weapons=['industrial', 'nonlethal'], melee=['industrial', 'medieval'], apparel=['civil', 'industrial'], anime='school', note='civilian clothes and police gear, standard firearms')
ROUGH = dict(weapons=['industrial', 'makeshift'], melee=['industrial', 'medieval'], apparel=['industrial', 'civil'], anime='school', note='flak and military surplus, firearms')
FRONTIER = dict(weapons=['frontier', 'industrial'], melee=['frontier', 'medieval'], apparel=['frontier', 'civil', 'industrial'], prefer=r'cowboy|duster|bandana|sombrero|flat cap|vest', anime='school', note='wild west: lever guns, revolvers, dusters, cowboy hats')
PIRATE = dict(weapons=['industrial', 'makeshift', 'spacer', 'laser'], melee=['industrial', 'medieval'], apparel=['industrial', 'civil', 'spacer'], note='scavenged military gear, higher ranks with stolen spacer gear')
WASTER = dict(weapons=['makeshift', 'industrial'], melee=['industrial', 'medieval'], apparel=['industrial', 'civil'], prefer=r'tox|gas mask', note='tox weapons, gas masks, makeshift guns')
YTTAKIN = dict(weapons=['industrial', 'makeshift'], melee=['industrial', 'medieval'], apparel=['civil', 'industrial', 'neolithic'], prefer=r'parka|tuque|duster|warm|fur', note='cold-weather clothes, firearms')
CANNIBAL = dict(weapons=['makeshift', 'industrial'], melee=['neolithic', 'medieval'], apparel=['industrial', 'civil'], prefer=r'mask|skull|bone', note='makeshift guns, crude melee, masks')
JUNKER = dict(weapons=['makeshift', 'industrial'], melee=['industrial', 'medieval'], apparel=['civil', 'industrial'], note='makeshift guns and patched clothes')
MERC = dict(weapons=['industrial', 'spacer', 'laser', 'greydale'], melee=['industrial', 'spacer'], apparel=['industrial', 'spacer', 'greydale'], anime='military', note='professional soldiers: modern and spacer weapons, combat armor')
GUILD = dict(weapons=['spacer', 'laser'], melee=['spacer', 'industrial'], apparel=['spacer', 'civil'], prefer=r'vacsuit|security', anime='scifi', note='spacer guards: vacsuits, security armor, charge guns')
IMPERIAL = dict(weapons=['imperial', 'spacer', 'laser', 'crypto'], melee=['imperial', 'ultra', 'spacer'], apparel=['imperial', 'spacer', 'ultra'], note='imperial soldiers: eltex and imperial attire, charge guns, mono and plasma blades; nobles keep their title gear')
FERAL = dict(weapons=['feral', 'makeshift'], melee=['feral', 'industrial'], apparel=['feral', 'civil'], note='Rimsenal Feral scrap guns and scrap armor')
FEDERATION = dict(weapons=['federation', 'spacer'], melee=['federation', 'spacer'], apparel=['federation', 'greydale', 'spacer'], note='Rimsenal Federation crucible guns and riot gear')
SPACER = dict(weapons=['rimspacer', 'spacer', 'laser'], melee=['rimspacer', 'spacer'], apparel=['rimspacer', 'greydale', 'spacer', 'civil'], prefer=r'smart|unisuit', anime='scifi', note='Rimsenal Spacer smart guns, spacer suits and armor')
ADVENTURER = dict(weapons=['medieval'], melee=['medieval'], apparel=['medieval', 'neolithic'], anime='fantasy', note='fantasy adventurers: swords, bows, wands, cloaks')

THEMES = {
    'TribeCivil': TRIBAL, 'TribeRough': TRIBAL, 'TribeSavage': TRIBAL, 'TribeCannibal': TRIBAL, 'TribeRoughNeanderthal': TRIBAL, 'TribeSavageImpid': TRIBAL,
    'NudistTribe': dict(TRIBAL, nude=True, note='bows and spears, still nude'),
    'VFEM2_CivilClan': CLAN, 'VFEM2_ClanRough': CLAN, 'VFEM2_ClanSavage': CLAN,
    'VFEM2_KingdomCivil': KINGDOM, 'VFEM2_KingdomRough': KINGDOM, 'VFEM2_KingdomSavage': KINGDOM, 'VFEM2_MerchantGuild': KINGDOM,
    'OutlanderCivil': UNION, 'OutlanderRough': ROUGH, 'OutlanderRoughPig': ROUGH, 'VRESaurids_OutlanderRoughSaurid': ROUGH,
    'SettlerCivil': FRONTIER, 'SettlerRough': FRONTIER, 'SettlerSavage': FRONTIER,
    'Pirate': PIRATE, 'PirateWaster': WASTER, 'PirateYttakin': YTTAKIN, 'CannibalPirate': CANNIBAL, 'VFEP_Junkers': JUNKER,
    'VFEP_Mercenaries': MERC, 'TradersGuild': GUILD, 'GuildFaction_AdventurersGuild': ADVENTURER,
    'Empire': IMPERIAL, 'Feral': FERAL, 'FPC': FEDERATION, 'SpacerCivil': SPACER, 'SpacerRough': SPACER,
}
OWN_GEAR_VISITORS = {}

MAX_WEAPONS = 10
MAX_POOL = 10
MAX_UNIFORMS = 60
MAX_HATS = 20


def money(text):
    parts = re.findall(r'[\d.]+', text or '')
    return float(parts[-1]) if parts else 0.0


def kind_role(k):
    t = tags(k, 'weaponTags')
    joined = ','.join(t)
    if not t or money(k['weaponMoney']) <= 0:
        return None
    if all(re.search('Melee|Bladelink|Axe|Sword', x) for x in t):
        return 'melee'
    if 'Grenade' in joined:
        return 'grenade'
    if re.search(r'GunHeavy|Minigun|HeavyGun|Flame|Rocket', joined):
        return 'heavy'
    if 'Sniper' in joined:
        return 'sniper'
    if re.search(r'ShortShots|Autopistol|Pistol', joined):
        return 'short'
    return 'gun'


def fits_everyone(a):
    return a['gender'] in ('None', '') and a['bodies'] in ('any', 'none', 'Male,Female,Thin,Hulk,Fat') and 'Adult' in a['stages']


def pick_weapons(weapons, theme, role, cap, kind_tags=()):
    buckets = theme['melee'] if role == 'melee' else theme['weapons']
    pool = []
    for wanted in {'heavy': ['heavy', 'gun'], 'sniper': ['sniper', 'gun'], 'short': ['short', 'gun'], 'grenade': ['grenade', 'heavy', 'gun'], 'gun': ['gun', 'short'], 'melee': ['melee']}[role]:
        pool = [w for w in weapons if w['bucket'] in buckets and w['role'] == wanted]
        if pool:
            break
    if not pool:
        return []
    # Weapons the kind's own tags accept keep its role; other themed weapons are only the fallback.
    tagged = [w for w in pool if set(tags(w, 'weaponTags')) & set(kind_tags)]
    if len(tagged) >= 3:
        pool = tagged
    prefer = theme.get('prefer')
    inside = [w for w in pool if w['value'] <= cap * 1.3] or sorted(pool, key=lambda w: w['value'])[:4]
    inside.sort(key=lambda w: -w['value'])
    if prefer:
        inside.sort(key=lambda w: 0 if re.search(prefer, w['label'], re.I) else 1)
    return inside[:MAX_WEAPONS]


def pick_apparel(apparel, theme, slots, cap, armored):
    pool = [a for a in apparel if a['bucket'] in theme['apparel'] and a['slot'] in slots and fits_everyone(a)]
    if not armored:
        pool = [a for a in pool if a['sharp'] < 0.45]
    inside = [a for a in pool if a['value'] <= cap] or sorted(pool, key=lambda a: a['value'])[:6]
    inside.sort(key=lambda a: -a['sharp'] if armored else -a['value'])
    prefer = theme.get('prefer')
    if prefer:
        inside.sort(key=lambda a: 0 if re.search(prefer, a['label'], re.I) else 1)
    return inside[:MAX_POOL]


def anime_sets(apparel, franchise):
    mods = set(FRANCHISE[franchise])
    usable = [a for a in apparel if a['mod'].lower() in mods and a['bucket'] == 'anime' and fits_everyone(a) and not re.search(r'shield|wedding', a['label'], re.I)]
    uniforms = [a for a in usable if a['slot'] == 'shell' and a['sharp'] <= 1.2 and {'Torso', 'Legs'} <= set(tags(a, 'parts'))]
    hats = [a for a in usable if a['slot'] == 'head']
    # Spread the cap over the franchise packs so one big pack does not fill the pool.
    def spread(rows, limit):
        by = collections.defaultdict(list)
        for r in rows:
            by[r['mod']].append(r)
        out = []
        while len(out) < limit and any(by.values()):
            for m in list(by):
                if by[m] and len(out) < limit:
                    out.append(by[m].pop(0))
        return out
    return spread(uniforms, MAX_UNIFORMS), spread(hats, MAX_HATS)


def li_gear(a):
    return f'<li><thingDefName>{a["defName"]}</thingDefName><weight>1</weight></li>'


def li_spec(a, mode, chance=None):
    c = f'<selectionChance>{chance}</selectionChance>' if chance is not None else ''
    return f'<li><thing>{a["defName"]}</thing><selectionMode>{mode}</selectionMode>{c}</li>'


def kind_xml(name, weapons, melee, specs, nude=False):
    x = [f'<li><kindDefName>{name}</kindDefName><isModified>True</isModified><forceIgnoreRestrictions>True</forceIgnoreRestrictions><outfitFirstBudgetStrategy>False</outfitFirstBudgetStrategy>']
    x.append(f'<forceOnlySelected>{"False" if nude else "True"}</forceOnlySelected>')
    if weapons:
        x.append('<weapons>' + ''.join(li_gear(w) for w in weapons) + '</weapons>')
    if melee:
        x.append('<meleeWeapons>' + ''.join(li_gear(w) for w in melee) + '</meleeWeapons>')
    if specs:
        x.append('<specificApparel>' + ''.join(specs) + '</specificApparel>')
    x.append('</li>')
    return ''.join(x)


def main(prefix, out, report):
    weapons, apparel = load(prefix)
    kinds = {k['defName']: k for k in table(prefix, 'kinds')}
    factions = {f['defName']: f for f in table(prefix, 'factions')}
    by_name = {a['defName']: a for a in apparel}
    faction_xml, rep = [], ['# Faction gear themes', '', 'Generated by factiontheme.py from a def dump. Item buckets are in classify.py.', '']
    used_kinds = 0
    for fname, f in factions.items():
        theme = THEMES.get(fname)
        franchise = (theme or {}).get('anime') or OWN_GEAR_VISITORS.get(fname)
        if not theme and not franchise:
            continue
        groups = re.findall(r'(\w+)\[o=([^ ]*) t=([^ ]*) c=([^ ]*) g=([^\]]*)\]', f['groups'])
        names = {x.split('*')[0] for g in groups for part in g[1:] for x in part.split(',') if x}
        peaceful = any(g[0] == 'Peaceful' for g in groups)
        entries = []
        skipped = []
        for name in sorted(names) if theme else []:
            k = kinds.get(name)
            required = [x for x in k['required'].split(',') if x] if k else []
            # A full required set is that kind's own design, like its title or psycaster gear; those keep their natural gear.
            if not k or k['race'] != 'Human' or 'Child' in name or len(required) >= 2 or k['specific'].strip(',') or 'Warcasket' in k['apparelTags'] or name.startswith('VFEE_Deserter') or name.endswith('_Fire') or 'Caster' in name or 'Royal' in name:
                skipped.append(name)
                continue
            role = kind_role(k)
            cap_w, cap_a, fighter = money(k['weaponMoney']), money(k['apparelMoney']), k['fighter'] == 'True'
            guns, blades = [], []
            if role == 'melee':
                blades = pick_weapons(weapons, theme, 'melee', cap_w, tags(k, 'weaponTags'))
            elif role:
                guns = pick_weapons(weapons, theme, role, cap_w, tags(k, 'weaponTags'))
            if role and not (guns or blades):
                skipped.append(name)
                continue
            specs = [li_spec(dict(defName=x), 'AlwaysTake') for x in required]
            taken = {by_name[x]['slot'] for x in required if x in by_name}
            if not theme.get('nude'):
                specs += [li_spec(a, 'FromPool1') for a in pick_apparel(apparel, theme, {'skin'} - taken, cap_a * 0.35, False)]
                specs += [li_spec(a, 'FromPool2') for a in pick_apparel(apparel, theme, {'legs'} - taken, cap_a * 0.35, False)]
                specs += [li_spec(a, 'FromPool3') for a in pick_apparel(apparel, theme, {'shell', 'middle'} - taken, cap_a * 0.8, fighter)]
                specs += [li_spec(a, 'FromPool4') for a in pick_apparel(apparel, theme, {'head'} - taken, cap_a * 0.4, fighter)]
            entries.append(kind_xml(name, guns, blades, specs, theme.get('nude', False)))
        visitors = ''
        if franchise and peaceful:
            uniforms, hats = anime_sets(apparel, franchise)
            specs = [li_spec(a, 'FromPool1') for a in uniforms]
            specs += [li_spec(a, 'RandomChance', round(0.3 / max(1, len(hats)), 4)) for a in hats]
            arms = theme or dict(weapons=['spacer', 'laser'], melee=['spacer'])
            for name, role, cap in (('LP17_Visitor', 'short', 250), ('LP17_VisitorGuard', 'gun', 600), ('LP17_VisitorElite', 'gun', 1500)):
                entries.append(kind_xml(name, pick_weapons(weapons, arms, role, cap), pick_weapons(weapons, arms, 'melee', cap), specs))
            visitors = f'{franchise} styles, {len(uniforms)} outfits, {len(hats)} hats'
        used_kinds += len(entries)
        faction_xml.append(f'<li><factionDefName>{fname}</factionDefName><isModified>True</isModified><kindGearData>' + ''.join(entries) + '</kindGearData></li>')
        rep += [f'## {f["label"]} (`{fname}`)', '', f'- Gear: {theme["note"] if theme else "own mod gear, unchanged"}', f'- Visitors: {visitors or "none (no visitor group)"}', f'- Kinds themed: {len(entries)}; left as they are: {", ".join(skipped) or "none"}', '']
    xml = ('<?xml version="1.0" encoding="utf-8"?>\n<SettingsBlock>\n<ModSettings Class="FactionGearCustomizer.FactionGearCustomizerSettings">\n'
           '<forceIgnoreRestrictions>True</forceIgnoreRestrictions>\n<autoRaidPointsEnabled>False</autoRaidPointsEnabled>\n<dismissedDialogs />\n<scenarioFactionConfig><selectedFactions /></scenarioFactionConfig>\n'
           '<factionGearData>\n' + '\n'.join(faction_xml) + '\n</factionGearData>\n<presets />\n</ModSettings>\n</SettingsBlock>\n')
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(xml)
    with open(report, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(rep))
    print('factions', len(faction_xml), 'kinds', used_kinds, 'xml bytes', len(xml))


if __name__ == '__main__':
    main(*sys.argv[1:4])
