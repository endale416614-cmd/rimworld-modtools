"""Sort every weapon and apparel from a probe dump (harness/proberun.ps1 -Mode dump) into theme buckets.

Run: py -I classify.py DUMP_PREFIX    prints bucket counts, examples and whatever nothing claimed.
factiontheme.py imports load() to build the faction editor settings.
"""
import csv
import re
import sys

csv.field_size_limit(10 ** 7)

ANIME_MODS = {m.lower() for m in """
mlmlmlm.cosplayequipmentbluearchive mlmlmlm.cosplayapparelgenshinimpact mlmlmlm.cosplayapparelazurlane
mlmlmlm.cosplayapparelhonkaistarrail mlmlmlm.cosplayapparelUmamusumePrettyDerby mlmlmlm.cosplayapparelprincessconnect
mlmlmlm.cosplayapparelcounterside mlmlmlm.cosplayequipmentgoddessofvictorynikke mlmlmlm.cosplayapparelwutheringwaves
mlmlmlm.cosplayapparelzenlesszonezero mlmlmlm.cosplayappareleternalreturn mlmlmlm.cosplayapparelcodevein
mlmlmlm.cosplayapparelgranbluefantasy RF.BanGDream.MYGOStylePack Mihoyo.GenshinImpact.CharacterStyles
Mihoyo.ZenlessZoneZero.CharacterStyles Kuro.WutheringWaves.CharacterStyles Snowbreak.ContainmentZone.CharacterStyles
RicoFox233.NL.GFPack RicoFox233.PTWZZ.GFPack RicoFox233.PTC.GFPack RicoFox233.PTCYL.GFPack RicoFox233.PNC.GFYTPack
RicoFox233.GirlsFrontline.DEFYTeam RicoFox233.GirlsFrontline.ARTeam RicoFox233.TouhouStyle.Youyoumu RicoFox233.BTR.StylePack
RicoFox233.MIA.WhiteflutePack RicoFox233.yuzu.LLLJCharacterStyles RicoFox.apparel.MGWTPack RF.ATELIERSOPHIE.CharacterStyles
Jupiter.EXILIUM Jupiter.madeinabyss zx.costumes MO.Size.Valentine.Apparel ponpeco.PonpecoAccessories
""".split()}

# Not armed or dressed by theme: things that are not really gear for a faction soldier.
SKIP_WEAPON_MODS = {m.lower() for m in """
wuhuansuiyue.defensivenetworkexpanded vanillaexpanded.gravship2 vanillaexpanded.gravship VanillaExpanded.VFESecurity rimsenal.security
Aoba.Fortress.Industrial.Citadel Aoba.Fortress.Industrial Aoba.Fortress.Medieval Aoba.Fortress.Frontier bambaryla.AllBambaTurrets
sarg.alphamechs Teiwaz.MVE MYDE.AerocraftFramework VE.VanillaChristmasExpanded XMB.Giantimperialcannonturret.MO pesky.ae
Aoba.Exosuit.PowerLoader setosa.power.tools MrKociak.YABEMBionicModularity GoGaTio.MechanoidUpgrades GoGaTio.MechanoidUpgrades.Archotech
forti.rimsiege VanillaExpanded.VBrewE VanillaExpanded.VAEEndAndExt VanillaExpanded.VGeneticsE vanillaquestsexpanded.ancients
vanillaracesexpanded.archon VanillaExpanded.VPersonaWeaponsE Poupun.IsekaiDungeonsAddon Poupun.IsekaiCreaturesAddon sarg.alphaanimals
OskarPotocki.VFE.Insectoid2 HaiLuan.AllsparkCrisis homebrew.fairycorecarillon homebrew.fairycorepixiewands
""".split()}
SKIP_WEAPON_TAGS = re.compile(r'^(TurretGun|Artillery.*|MVM_MechWeapon|Mechanoid.*|ChargeBlasterHeavyGun|DMS_.*|A_SuppressWeapon|Bladelink|ArchobladeBladelink|Warcasket.*|AdvancedTool|DroneBomb|AM_.*)$')

SKIP_APPAREL_MODS = {m.lower() for m in """
Seti.Medieval.Trinkets Ingendum.AnimalArmorBasic Dubwise.DubsRimkit Aoba.Exosuit.PowerLoader Aoba.Exosuit.Framework pesky.ae
zal.morearchotechgarbage GoGaTio.MechanoidUpgrades.Archotech migcorp.skiptech sarg.alphamechs MYDE.AerocraftFramework
wuhuansuiyue.defensivenetworkexpanded vanillaquestsexpanded.ancients vanillaquestsexpanded.generator VanillaExpanded.VAnomalyEInsanity
VanillaExpanded.VMemesE HaiLuan.AllsparkCrisis vanillaracesexpanded.archon vanillaracesexpanded.fungoid leafzxg.ThrumboExtension
LTS.PS rimsenal.security Rimsenal.Askbarn metalocif.cosmicmonoliths Smxrez.cryptorestored CAT.CryptoPack
OskarPotocki.VFE.Deserters VanillaExpanded.VWEHW
Malistaticy.UnderWhere ponpeco.KidsClothes homebrew.fairycoreapparel2 Ludeon.RimWorld.Anomaly darjeeling.BattleCloak Aoba.VariousApparelV1
kikohi.jewelry Akarthus.TheCosmicOdyssey.AccessoryExpanded CatLover366.BootsAndGloves Kilo.CapVacHelm VanillaExpanded.VAEAccessories
""".split()}
# Job or role clothes that look wrong handed out at random, whatever the faction.
ODD_APPAREL = re.compile(r"chef|doctor|scrubs|builder|lab coat|hazmat|slave|strap|jester|king's|crown|plague|bishop|burka|rumsuit|general|captain|hardhat|gravship|prestige|psychic|sheriff|shirt and tie|suit jacket|bandol|bandoleer|wedding|patient|gag|straitjacket|tackle|cerebrex|foil|bubble helmet|tiara|mitre|top hat|bowler|fez|apron|surgical mask|overalls|sash|scarf|flak collar|skirt|blouse", re.I)
SKIP_APPAREL_TAGS = re.compile(r'^(Warcasket.*|Exosuit.*|Mechlord|AnimalApparel|AnimalOnly|Horaxian.*|VAE_.*|VQE_.*|Archon|AncientAstrosuit|Seti_Trinkets|MedicalKit|RepairKit|GrenadeNonDestructiveBelt)$')


def table(prefix, name):
    with open(prefix + '.' + name + '.tsv', encoding='utf-8') as f:
        return list(csv.DictReader(f, delimiter='\t', quoting=csv.QUOTE_NONE))


def tags(row, col):
    return [t for t in row.get(col, '').split(',') if t]


def weapon_role(w):
    t = set(tags(w, 'weaponTags'))
    label = w['label'].lower()
    if w['kind'] == 'melee':
        return 'melee'
    if any(x.startswith('Grenade') for x in t) or 'grenade' in label or 'molotov' in label:
        return 'grenade'
    if t & {'GunHeavy', 'HeavyGun', 'Minigun', 'GunSingleUse', 'HeavyTox', 'RocketLauncher'} or re.search(r'minigun|rocket|launcher|mortar|flamer|flamethrower|autocannon', label):
        return 'heavy'
    if 'SniperRifle' in t or 'sniper' in label:
        return 'sniper'
    try:
        rng = float(w['range'] or 0)
    except ValueError:
        rng = 0
    if t & {'ShortShots', 'Autopistol', 'Pistol'} or 0 < rng < 17:
        return 'short'
    return 'gun'


def weapon_bucket(w):
    mod = w['mod'].lower()
    t = tags(w, 'weaponTags')
    if mod in ANIME_MODS or 'BAGun' in t or 'BAIndustrialGunAdvanced' in t or 'NikkeWeapon' in t or 'RF_Weapons' in t:
        return 'anime'
    if mod in SKIP_WEAPON_MODS or not mod or w['tech'] == 'Undefined' or w['destroyOnDrop'] == 'True' or any(SKIP_WEAPON_TAGS.match(x) for x in t):
        return 'skip'
    if re.search(r'Drugs|Foods|ResourcesRaw|Manufactured|Chunk|Stone', w['categories']) or w['label'].startswith('unique ') or w['label'].startswith('['):
        return 'skip'
    if mod == 'rimsenal.federation':
        return 'federation'
    if mod == 'rimsenal.feral':
        return 'feral'
    if mod in ('rimsenal.spacer', 'rimsenal.harana', 'rimsenal.askbarn', 'rimsenal.zohar', 'rimsenal.dioktri'):
        return 'rimspacer'
    if mod == 'rimsenal.core' and 'GDGun' in t:
        return 'greydale'
    if mod in ('oskarpotocki.vfe.empire', 'oskarpotocki.vfe.deserters', 'cat.stellicarsenal') or mod.startswith('cat.') or mod == 'ludeon.rimworld.royalty':
        return 'imperial'
    if mod in ('smxrez.cryptorestored', 'vanillaquestsexpanded.cryptoforge'):
        return 'crypto'
    if mod in ('vanillaexpanded.vweft', 'oskarpotocki.vanillafactionsexpanded.settlersmodule') or 'FrontierGunSimple' in t or 'FrontierGunAdvanced' in t:
        return 'frontier'
    if mod == 'vanillaexpanded.vwems' or 'MakeshiftGun' in t or mod == 'vanillaquestsexpanded.dronefactory':
        return 'makeshift'
    if mod == 'vanillaexpanded.vwebf':
        return 'bioferrite'
    if mod == 'vanillaexpanded.vwenl':
        return 'nonlethal'
    if mod == 'vanillaexpanded.vwel':
        return 'laser'
    return {'Neolithic': 'neolithic', 'Medieval': 'medieval', 'Industrial': 'industrial', 'Spacer': 'spacer', 'Ultra': 'ultra', 'Archotech': 'ultra'}.get(w['tech'], 'skip')


def apparel_slot(a):
    layers, parts = set(tags(a, 'layers')), set(tags(a, 'parts'))
    if 'Belt' in layers:
        return 'belt'
    if 'Overhead' in layers:
        return 'head'
    if layers & {'EyeCover', 'StrappedHead'} or (parts and parts <= {'FullHead', 'UpperHead', 'Eyes', 'Teeth', 'Mouth', 'Nose', 'Neck'}):
        return 'extra'
    if 'Shell' in layers:
        return 'shell'
    if 'Middle' in layers and parts & {'Torso'}:
        return 'middle'
    if parts & {'Torso'}:
        return 'skin'
    if parts & {'Legs'}:
        return 'legs'
    return 'extra'


def apparel_bucket(a):
    mod = a['mod'].lower()
    t = tags(a, 'apparelTags')
    if mod in ANIME_MODS:
        return 'anime'
    if mod in SKIP_APPAREL_MODS or not mod or any(SKIP_APPAREL_TAGS.match(x) for x in t) or a['stages'] in ('Child', 'Baby', 'Baby, Child', 'Child, Baby'):
        return 'skip'
    if a['bodies'] == 'none' and apparel_slot(a) not in ('belt', 'extra', 'legs'):
        return 'skip'
    if ODD_APPAREL.search(a['label']):
        return 'skip'
    if mod == 'rimsenal.federation':
        return 'federation'
    if mod == 'rimsenal.feral':
        return 'feral'
    if mod in ('rimsenal.spacer', 'rimsenal.harana'):
        return 'rimspacer'
    if mod == 'rimsenal.core':
        return 'greydale'
    if mod in ('oskarpotocki.vfe.empire', 'cat.imperial.attires', 'ludeon.rimworld.royalty') or 'Royal' in t and a['tech'] in ('Spacer', 'Ultra'):
        return 'imperial'
    if mod in ('oskarpotocki.vanillafactionsexpanded.settlersmodule',):
        return 'frontier'
    if mod == 'vanillaquestsexpanded.cryptoforge':
        return 'crypto'
    if mod in ('oskarpotocki.vfe.tribals', 'river.tribal.shoes') or 'TribalAdvanced' in t:
        return 'neolithic'
    if mod in ('gerrymon.greekcoremedieval', 'gerrymon.mco', 'oskarpotocki.vfe.medieval2', 'vanillaexpanded.viehar', 'addie.addiesmantlesandcloaks', 'erin.cottagecollection'):
        return 'neolithic' if a['tech'] == 'Neolithic' else 'medieval'
    if 'IndustrialBasic' in t and a['tech'] in ('Medieval', 'Industrial'):
        return 'civil'
    return {'Neolithic': 'neolithic', 'Medieval': 'medieval', 'Industrial': 'industrial', 'Spacer': 'spacer', 'Ultra': 'ultra', 'Archotech': 'ultra'}.get(a['tech'], 'skip')


def load(prefix):
    weapons, apparel = table(prefix, 'weapons'), table(prefix, 'apparel')
    for w in weapons:
        w['bucket'] = weapon_bucket(w)
        w['role'] = weapon_role(w)
        w['value'] = float(w['value'] or 0)
    for a in apparel:
        a['bucket'] = apparel_bucket(a)
        a['slot'] = apparel_slot(a)
        a['value'] = float(a['value'] or 0)
        a['sharp'] = float(a['sharp'] or 0)
    return weapons, apparel


if __name__ == '__main__':
    weapons, apparel = load(sys.argv[1])
    import collections
    print('== weapons', len(weapons))
    by = collections.defaultdict(list)
    for w in weapons:
        by[w['bucket']].append(w)
    for b, rows in sorted(by.items(), key=lambda kv: -len(kv[1])):
        roles = collections.Counter(r['role'] for r in rows)
        mods = collections.Counter(r['mod'] for r in rows).most_common(5)
        print(f'{b:11s} {len(rows):4d} {dict(roles)}\n     mods {mods}\n     e.g. {[r["label"][:16] for r in rows[:9]]}')
    print('== apparel', len(apparel))
    by = collections.defaultdict(list)
    for a in apparel:
        by[a['bucket']].append(a)
    for b, rows in sorted(by.items(), key=lambda kv: -len(kv[1])):
        slots = collections.Counter(r['slot'] for r in rows)
        mods = collections.Counter(r['mod'] for r in rows).most_common(5)
        print(f'{b:11s} {len(rows):4d} {dict(slots)}\n     mods {mods}\n     e.g. {[r["label"][:16] for r in rows[:9]]}')
    anime = by['anime']
    print('== anime armor', sorted({round(a['sharp'], 1) for a in anime}), 'partial bodies', sum(1 for a in anime if a['slot'] != 'head' and a['bodies'] != 'Male,Female,Thin,Hulk,Fat'), 'gendered', sum(1 for a in anime if a['gender'] not in ('None', '')))
    print('anime layers', collections.Counter(a['layers'] for a in anime).most_common(10))
    print('anime parts', collections.Counter(a['parts'] for a in anime).most_common(8))
    print('stages', collections.Counter(a['stages'] for a in apparel).most_common(6))
    print('sample worst armor', [(a['label'][:18], a['sharp'], a['mod'][:20]) for a in sorted(anime, key=lambda a: -a['sharp'])[:10]])
