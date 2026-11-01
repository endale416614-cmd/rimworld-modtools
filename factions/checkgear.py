"""Check a probe gear table (harness/proberun.ps1 -Mode gear) against the rules in factiontheme.py.

Run: py -I checkgear.py DUMP_PREFIX GEAR_PREFIX
Anime apparel must appear on visitor kinds only, visitors must wear it, and themed kinds must stay inside their theme.
"""
import collections
import csv
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from classify import load
from factiontheme import THEMES, OWN_GEAR_VISITORS

VISITOR_KINDS = {'LP17_Visitor', 'LP17_VisitorGuard', 'LP17_VisitorElite'}


def main(dump, gear):
    weapons, apparel = load(dump)
    w = {x['defName']: x for x in weapons}
    a = {x['defName']: x for x in apparel}
    with open(gear + '.gear.tsv', encoding='utf-8') as f:
        rows = list(csv.DictReader(f, delimiter='\t', quoting=csv.QUOTE_NONE))
    print('pawns', len(rows), 'factions', len({r['faction'] for r in rows}))
    anime_off, visitor_plain, naked, unarmed, off_weapon, off_apparel = [], [], collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter()
    per = collections.defaultdict(lambda: dict(pawns=0, visitors=0, anime=0, weapons=collections.Counter(), apparel=collections.Counter()))
    for r in rows:
        worn = [x for x in r['apparel'].split(',') if x]
        anime = [x for x in worn if a.get(x, {}).get('bucket') == 'anime']
        fac, kind = r['faction'], r['kind']
        p = per[fac]
        p['pawns'] += 1
        is_visitor = kind in VISITOR_KINDS
        if is_visitor:
            p['visitors'] += 1
            p['anime'] += bool(anime)
            if not anime:
                visitor_plain.append((fac, kind, r['gender'], r['body'], worn[:4]))
        elif anime:
            anime_off.append((fac, kind, r['group'], anime[:3]))
        theme = THEMES.get(fac)
        if r['weapon']:
            b = w.get(r['weapon'], {}).get('bucket', '?')
            p['weapons'][b] += 1
        if theme and not is_visitor:
            if r['weapon'] and r['weapon'] in w and w[r['weapon']]['bucket'] not in set(theme['weapons']) | set(theme['melee']):
                off_weapon[(fac, kind, w[r['weapon']]['label'][:22])] += 1
            for x in worn:
                if x in a and not theme.get('nude') and a[x]['bucket'] not in set(theme['apparel']) and a[x]['mod'] != 'Malistaticy.UnderWhere' and a[x]['slot'] in ('skin', 'legs', 'shell', 'middle', 'head'):
                    off_apparel[(fac, kind, a[x]['label'][:22])] += 1
            torso = [x for x in worn if x in a and a[x]['slot'] in ('skin', 'shell', 'middle')]
            if not torso and not theme.get('nude'):
                naked[(fac, kind)] += 1
        if theme and not r['weapon'] and not is_visitor:
            unarmed[(fac, kind)] += 1
    print('RULE anime on a non-visitor:', len(anime_off), anime_off[:12])
    print('RULE visitor without anime:', len(visitor_plain), visitor_plain[:12])
    print('no torso clothes (themed kinds):', sum(naked.values()), naked.most_common(12))
    print('unarmed (themed kinds):', sum(unarmed.values()), unarmed.most_common(14))
    print('weapon outside theme:', sum(off_weapon.values()), off_weapon.most_common(14))
    print('apparel outside theme:', sum(off_apparel.values()), off_apparel.most_common(14))
    print('== per faction: pawns | visitors with anime | weapon buckets')
    for fac, p in sorted(per.items()):
        mark = 'themed' if fac in THEMES else 'own gear' if fac in OWN_GEAR_VISITORS else 'unchanged'
        print(f"{fac:34s} {mark:9s} {p['pawns']:4d} | {p['anime']}/{p['visitors']} | {p['weapons'].most_common(4)}")


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
