# Bloodsucker 9000 v1.2.0 — BS-7500 Drain Lance and BG-9555 Hemogen Tank

Target: RimWorld 1.7.5011, Biotech required, no Harmony (same constraint as v1.1.0).

Source: `F:\Development\rimworld-modtools\bs9000`
Built copy: `C:\Users\xxmer\Desktop\Bloodsucker 9000`
Installed copy: `F:\Steam\steamapps\common\RimWorld\Mods\09-medical__local.hemogen.tap`

---

## 1. Verified engine facts this design rests on

All quoted from `F:\Steam\steamapps\common\RimWorld\Data` on 1.7.5011, or from IL of `Assembly-CSharp.dll`.

### Lances are apparel, not weapons

`Apparel_PsychicShockLance` (`Core\Defs\ThingDefs_Misc\Apparel_Utility.xml`) is a `ThingDef ParentName="ApparelNoQualityBase"`:

- `apparel.layers` = `Belt`, `apparel.bodyPartGroups` = `Waist`
- `verbs` = one `Verb_CastTargetEffectLances`, `targetable true`, `onlyManualCast true`, `range 41.9`, `warmupTime 2.2`
- no projectile at all — the effect is delivered by `CompTargetEffect_*` comps
- `tickerType` = `Normal`
- expendability comes from a comp, not the verb class:

```xml
<li Class="CompProperties_ApparelReloadable">
  <maxCharges>2</maxCharges>
  <destroyOnEmpty>true</destroyOnEmpty>
  <hotKey>Misc4</hotKey>
  <chargeNoun>charge</chargeNoun>
  <displayGizmoWhileUndrafted>false</displayGizmoWhileUndrafted>
</li>
```

So `maxCharges 1` + `destroyOnEmpty true` is exactly "the lance breaks after use", with no code.

`Verb_ShootOneUse` exists too (`Gun_TripleRocket`, `Gun_DoomsdayRocket`) but that is the one-use *gun* pattern, with a projectile. The lance pattern is the right base.

### There is no `Pack` apparel layer

`Core\Defs\Misc\ApparelLayerDefs\ApparelLayerDefs.xml` defines exactly six layers: `OnSkin`, `Middle`, `Shell`, `Belt`, `Overhead`, `EyeCover`. No expansion adds one.

Every vanilla backpack (jump pack, broadshield pack, hunter pack, tox pack, firefoam pack) is `Belt` layer + `Waist` body part group, and is drawn on the back purely by:

```xml
<wornGraphicData>
  <renderUtilityAsPack>true</renderUtilityAsPack>
  ...
</wornGraphicData>
```

**Consequence:** two Belt-layer items that both claim `Waist` cannot be worn together. The fix needs no new layer. An apparel conflict requires both the same layer *and* an overlapping body part group, so `Belt` + `Torso` coexists with `Belt` + `Waist`.

This is exactly what the installed mod **Packs Are Not Belts** (`turnovus.submod.backpacksarenotbelts`, active in this load order) does. It never adds a layer for this; it rewrites the body part group, e.g. `1.6\DLC\Royalty\Patches\RoyaltyPacks.xml`:

```xml
<li Class="PatchOperationReplace">
  <xpath>/Defs/ThingDef[defName="Apparel_PackJump"]/apparel/bodyPartGroups</xpath>
  <value>
    <bodyPartGroups>
      <li>Torso</li>
    </bodyPartGroups>
  </value>
</li>
```

So: **Drain Lance = `Belt` + `Waist`** (vanilla lance slot), **Hemogen Tank = `Belt` + `Torso`** (the pack slot). They stack, with no new `ApparelLayerDef`, no soft dependency and no fallback def needed — the behavior comes from vanilla and holds whether or not Packs Are Not Belts is loaded.

The custom layers that mod *does* add (`PacksAreNotBelts_Tactical`, `_Ammo`, `_Exoskeleton`, `_Mechanitor`, `_Artifact`, `_LifeSupport`) and the one VFE Core adds (`VFEC_OuterShell`, drawOrder 500) are deliberately not used, since depending on them would make the tank unwearable without those mods.

Known collision, accepted: the tank and a jump pack / broadshield pack both want `Belt` + `Torso` once Packs Are Not Belts is active. One pack per back is correct.

### Reloadable apparel can consume a real item as ammo

`Apparel_PackJump`, `ArmorLocust` (ammoDef `Chemfuel`), `ArmorMarineGrenadier` (ammoDef `Steel`), `DisruptorFlarePack` (ammoDef `Bioferrite`) all use `CompProperties_ApparelReloadable` with `ammoDef` + `ammoCountPerCharge` + `chargeNoun`.

So `ammoDef HemogenPack`, `ammoCountPerCharge 1` gives the tank its manual refill for free. Charges are integers, not stored item instances — functionally identical for this design and far smaller.

No vanilla apparel stores real items inside itself. The only `CompProperties_ThingContainer` users are buildings.

### Hemogen API

- `RimWorld.Gene_Hemogen : Gene_Resource`. Field `hemogenPacksAllowed` (bool).
- Add path is static: `GeneUtility.OffsetHemogen(Pawn pawn, float amount, bool applyGainFactor)`. There is no `OfferHemogen`.
- `Gene_Hemogen.ShouldConsumeHemogenNow()` returns `Value < targetValue`.
- `targetValue` is the player-set threshold on the hemogen gizmo (`SetTargetValuePct`, clamped to `[0, max - 0.1]`).
- `Gene_Hemogen.InitialResourceMax` = 1. One hemogen pack = `IngestionOutcomeDoer_OffsetHemogen` with `offset 0.2`.
- `JobGiver_GetHemogen` already auto-consumes packs from the pawn's carried thing, then the pawn's own inventory, then the map. It does **not** look inside apparel.
- `Workgiver_AdministerHemogen` (WorkGiverDef `FeedHemogen`, Doctor work) feeds a *hemogenic* patient when `ValuePercent < 0.95`, sourcing packs via `ClosestThingReachable` on the map. It also does not look inside apparel.

**This settles design note \*1 in the brief.** The "only check once below this bar" behavior already exists as `targetValue`, player-adjustable per pawn. The tank just has to gate on `ShouldConsumeHemogenNow()` instead of on `Value < Max`, and it will never top a pawn up from 99/100.

### Hemogen pack and extraction

`HemogenPack` (`Biotech\Defs\ThingDefs_Items\Items_Food.xml`): `stackLimit 10`, `Mass 0.50`, `MarketValue 5`.

`RecipeDef ExtractHemogenPack`, worker `Recipe_ExtractHemogen`: requires a non-hemogenic, non-baby target with `BloodLoss` severity below 0.45; adds 0.45 `BloodLoss`; drops one pack on the floor. This is the per-pack blood cost the Drain Lance should be measured against.

### Load-order neighbour

`09-medical__aoba.hemogendirect` is installed and active. It patches `CompUsable` onto `HemogenPack` so a player can right-click "Administer ..." a pack onto a pawn (JobDef `HD_AdministerHemogen`). That is a manual player order only. It does not conflict with an automatic tank, and it does not already provide the tank's behavior.

---

## 2. BS-7500 Drain Lance

Flavor from the brief: a disposable megacorp "compliance tool". Kills the subject, liquefies them into hemogen packs, breaks on use.

### Defs (`Defs/BS9000.xml`)

`ThingDef BS7500_DrainLance`, `ParentName="ApparelNoQualityBase"`, modelled on `Apparel_PsychicShockLance`:

| Field | Value | Reason |
|---|---|---|
| `techLevel` | `Industrial` | megacorp riot gear, not archotech |
| `thingCategories` | `ApparelUtility` | same shelf as the vanilla lances |
| `tickerType` | `Normal` | required for the verb/comp |
| `statBases` | `Mass 2.5`, `EquipDelay 1`, `MaxHitPoints 60`, `WorkToMake 6000` | heavy-duty disposable |
| `costList` | `Steel 30`, `ComponentIndustrial 2` | cheap enough to be disposable |
| `apparel.layers` | `Belt` | |
| `apparel.bodyPartGroups` | `Waist` | the vanilla lance slot; leaves `Torso` free for the tank |
| `apparel.wornGraphicPath` | `Things/Item/BS9000/DrainLance` | reuse the item texture, as the vanilla lances do |
| `recipeMaker.researchPrerequisite` | `BS9000` | reuses the existing research project |
| `recipeMaker.recipeUsers` | `FabricationBench` | matches the existing implant |

Verb:

```xml
<verbs>
  <li>
    <verbClass>Verb_CastTargetEffectLances</verbClass>
    <label>drain lance</label>
    <hasStandardCommand>true</hasStandardCommand>
    <targetable>true</targetable>
    <onlyManualCast>True</onlyManualCast>
    <range>7.9</range>
    <warmupTime>3.5</warmupTime>
    <targetParams>
      <canTargetBuildings>false</canTargetBuildings>
      <canTargetAnimals>false</canTargetAnimals>
    </targetParams>
    <soundCast>Bloodfeed_Cast</soundCast>
  </li>
</verbs>
```

`range 7.9` and `warmupTime 3.5` implement the brief's "short radius, long aim-time, close quarters". Note the vanilla lances set `neverTargetIncapacitated true`; this one deliberately omits it, so **both downed and standing targets are valid** — the brief's note \*2.

Comps:

```xml
<comps>
  <li Class="CompProperties_ApparelReloadable">
    <maxCharges>1</maxCharges>
    <destroyOnEmpty>true</destroyOnEmpty>
    <hotKey>Misc4</hotKey>
    <chargeNoun>charge</chargeNoun>
    <displayGizmoWhileUndrafted>false</displayGizmoWhileUndrafted>
  </li>
  <li Class="BS9000.CompProperties_DrainLance">
    <packsPerBodySize>6</packsPerBodySize>
  </li>
  <li Class="CompProperties_TargetEffect_GoodwillImpact">
    <goodwillImpact>-200</goodwillImpact>
  </li>
</comps>
```

### Code (`DrainLance.cs`, new file, namespace `BS9000`)

One class, `CompTargetEffect_DrainLance : CompTargetEffect`, overriding `DoEffectOn(Pawn user, Thing target)`:

1. Reject anything that is not a fleshy pawn with a `BloodDef` (`pawn.RaceProps.IsFlesh && BloodDef != null`). Mechs and non-bleeders are not profitable assets.
2. Fire the existing `BS9000_Bite` effecter via `SpawnAttached(pawn, map).Cleanup()` — already defined in v1.1.0, already proven, no new effecter needed.
3. Blood filth at the target cell via `FilthMaker.TryMakeFilth`.
4. Yield = `Mathf.Max(1, Mathf.RoundToInt(packsPerBodySize * pawn.BodySize))`, so an adult human (BodySize 1.0) gives 6 packs. Compare: `Recipe_ExtractHemogen` costs 0.45 blood loss for one pack, so 6 packs is roughly "all of them", which is the intent.
5. `pawn.Kill(new DamageInfo(DamageDefOf.Cut, 99999f, ...), null)` with the user as instigator, so the kill is attributed and generates the normal social/ideo fallout.
6. `GenPlace.TryPlaceThing` the hemogen packs near the corpse, in stacks of `HemogenPack.stackLimit` (10).
7. Message via a new keyed string `BS7500_Drained`.

The lance destroying itself is handled entirely by `destroyOnEmpty` — no code.

**Balance risk to watch:** a guaranteed kill on a standing pawn is strong. The 7.9 range and 3.5 s warmup are the only brakes. If it plays too well, raise `warmupTime` rather than adding a failure chance, so the flavor ("the process kills the subject") stays literal.

---

## 3. BG-9555 Hemogen Tank

### Defs

`ThingDef BG9555_HemogenTank`, `ParentName="ApparelNoQualityBase"`, modelled on `Apparel_PackJump`:

| Field | Value |
|---|---|
| `techLevel` | `Industrial` |
| `thingCategories` | `ApparelUtility` |
| `tickerType` | `Normal` |
| `statBases` | `Mass 4`, `EquipDelay 2`, `MaxHitPoints 100`, `WorkToMake 9000` |
| `costList` | `Steel 50`, `ComponentIndustrial 3` |
| `apparel.layers` | `Belt` |
| `apparel.bodyPartGroups` | `Torso` — the pack slot, so the tank and the lance stack |
| `apparel.wornGraphicPath` | `Things/Pawn/Humanlike/Apparel/HemogenTank/HemogenTank` |
| `apparel.wornGraphicData` | `renderUtilityAsPack true`, plus the per-direction/body-type offsets copied from `Apparel_PackJump` as a starting point |
| `recipeMaker` | `researchPrerequisite BS9000`, `recipeUsers FabricationBench` |

Storage and manual refill, free from vanilla:

```xml
<li Class="CompProperties_ApparelReloadable">
  <maxCharges>10</maxCharges>
  <ammoDef>HemogenPack</ammoDef>
  <ammoCountPerCharge>1</ammoCountPerCharge>
  <baseReloadTicks>120</baseReloadTicks>
  <destroyOnEmpty>false</destroyOnEmpty>
  <chargeNoun>hemogen pack</chargeNoun>
  <displayGizmoWhileUndrafted>true</displayGizmoWhileUndrafted>
</li>
```

### Code (`HemogenTank.cs`, new file, namespace `BS9000`)

**Behavior A — wearer self-refill (small).**
`Comp_HemogenTank : ThingComp`, ticking rare (250 ticks) while worn:

- find the wearer's `Gene_Hemogen`; bail if absent
- bail unless `gene.hemogenPacksAllowed && gene.ShouldConsumeHemogenNow()`
- bail unless the sibling `CompApparelReloadable.RemainingCharges > 0`
- `UsedOnce()` to spend a charge, then `GeneUtility.OffsetHemogen(wearer, 0.2f, true)` — the same 0.2 the vanilla ingestion doer gives

`ShouldConsumeHemogenNow()` is what keeps this off the 99/100 treadmill, and it honours the per-pawn threshold slider the player already has.

**Behavior B — auto-transfuse to patients (larger).** Confirmed: full automatic.

A `WorkGiverDef BS9000_TankTransfuse` (Doctor work, priority just above `FeedHemogen`) plus `WorkGiver_TankTransfuse : WorkGiver_Scanner`, a `JobDef` and a `JobDriver`. The scanner mirrors `Workgiver_AdministerHemogen` but sources charges from the worn tank instead of `ClosestThingReachable`, so no map pack is needed and no Harmony patch is required. Two target cases:

1. hemogenic patient, `ValuePercent < 0.95` and `ShouldConsumeHemogenNow()` → `GeneUtility.OffsetHemogen(patient, 0.2f, true)`
2. any bleeding patient with `BloodLoss` severity ≥ 0.25 → `HealthUtility.AdjustSeverity(patient, HediffDefOf.BloodLoss, -0.25f)`, matching the pack's stated "administered via an operation to reverse blood loss"

Estimated size: roughly 80–110 lines plus 2 defs. This is the single biggest piece of the release.

---

## 4. Textures

Source art: `C:\Users\xxmer\Desktop\drain lance.png` and `hemogen tank.png`, both 1500x1500 RGBA. (`Hemogen mod.zip` on the Desktop holds the same two files and nothing else.)

Convention in this mod library is 256x256 PNG. Pillow is not installed; `tools\Make-Preview.ps1` already proves System.Drawing works here, so a small `tools\Make-Textures.ps1` does the job: trim to the alpha bounding box, pad square, resample to 256x256 `HighQualityBicubic`, write PNG.

Output layout:

```
Textures/Things/Item/BS9000/DrainLance.png                        256x256
Textures/Things/Item/BS9000/HemogenTank.png                       256x256
Textures/Things/Pawn/Humanlike/Apparel/HemogenTank/HemogenTank.png 256x256
```

The worn pack needs only the base file; `_north` / `_south` / `_east` are optional per-direction overrides (the vanilla jump pack ships them, and ships no `_west`). Start with the base file alone and add directions only if the back-mounted render looks wrong.

The lance art reads as a gun held diagonally. Worn on the waist it will look like the vanilla lances do — acceptable, and the same texture serves both the item and the worn overlay.

---

## 5. Decisions — settled

**D1 — slot.** Lance on `Belt` + `Waist`, tank on `Belt` + `Torso`. Both wearable at once. No new `ApparelLayerDef`, no dependency on any other mod, no fallback def. Resolved in section 1.

**D2 — transfuse scope.** Full automatic work giver.

**D3 — Drain Lance acquisition.** Craftable at a fabrication bench under the existing `BS9000` research project.

---

## 6. Work order

1. `tools\Make-Textures.ps1`, generate the three PNGs, eyeball them in-game at 1x zoom.
2. Defs for both items, plus keyed strings. XML-only load test — the lance already works at this point (verb + `destroyOnEmpty`) apart from its effect.
3. `DrainLance.cs`. Add a debug action mirroring the existing `"Bloodsucker 9000: tap now"` so the effect can be fired without crafting one.
4. `HemogenTank.cs` behavior A.
5. `HemogenTank.cs` behavior B: work giver, job def, job driver.
6. Bump `About.xml` to `1.2.0`, extend the description.
7. Build `BS9000.dll` for 1.7, sync source → Desktop copy → installed copy.
8. Launch test and probe. Nothing here is save-breaking: both items are plain apparel, so removing the mod only loses the items.

### Self-check for this release

Add to the existing debug actions: one that spawns both items and one that drains a selected pawn, so the kill-and-yield path and the refill path can each be fired in under a minute without a crafting run. New non-trivial logic here is the yield calculation and the charge accounting, and both are observable from those two actions.

---

## 7. What changed during the build

**Worn apparel comps are never ticked.** `Pawn.TickInterval` reaches `Pawn_ApparelTracker.ApparelTrackerTickInterval`, and that method only records the nudity tale and wears down locked apparel. It never touches the worn items' comps, and nothing else calls `get_WornApparel` from a tick path. `CompApparelReloadable.CompTick` therefore only runs while the item lies on the ground.

So the tank's self-refill could not live on a comp tick as section 3 planned. It runs from `MapComponent_HemogenTanks` instead, every 250 ticks over `map.mapPawns.AllPawnsSpawned`. Same behavior, guaranteed to run.

**A crafted item starts full.** `CompApparelVerbOwner_Charged.PostPostMake` sets `remainingCharges = MaxCharges` unconditionally, with no regard for `ammoDef`. A newly crafted tank therefore arrives holding ten hemogen packs, and a newly crafted lance arrives charged. Vanilla does the same (a fresh firefoam pack comes with its charge of chemfuel already in it), and ten packs are worth 50 silver against a 50 steel + 3 component tank, so this is not worth special-casing. Worth knowing before it reads as a bug.

**The verb spends the charge, not the comp.** `Verb_CastTargetEffect.TryCastShot` runs every `CompTargetEffect` on the equipment and then calls `ReloadableCompSource.UsedOnce()`. The drain comp must not spend a charge itself or the lance would consume two.

**Plain `Verb_CastTargetEffect`, not `Verb_CastTargetEffectLances`.** The `Lances` subclass only adds `ValidateTarget` and `OnGUI` for psychic immunity, which does not apply here.

## 8. Test results

**Launch test passed: 83 checks ok, 0 FAIL**, `proberun.ps1 -Mode hemogentap -Save "Laniakea Corporation"`, 2026-11-17, game 1.7.5011 rev24, 561 active mods, VERDICT done after 164s. The only ERROR in the log is the pre-existing `[Likely: LocalPack Hotfixes] Failed to find sane damage amount`, unrelated to this mod.

What the run proved, verbatim from the log:

```
drain lance on Rhubarb (body size 1.00): dead True, packs 1 -> 7, expected +6
tank transfuse on Jenson: ok True, charges 10 -> 9, blood loss 0.400 -> 0.150
tank hemogen on Artur: ok True, charges 9 -> 8, hemogen 0.200, target 0.500
transfuse giver on Hornbean: downed True, blood loss 0.500, offered True, job BS9000_TankTransfuse
```

Both defs load and both recipes are gated on the `BS9000` research at the fabrication bench. Lance and tank are wearable together. Every texture resolves, ground and worn, in all directions. The drain kills its subject and yields packs scaled to body size; the lance destroys itself when its charge is spent and leaves the wearer. The tank arrives full, takes hemogen packs as ammo, spends exactly one charge per transfusion, cuts blood loss, refuses a patient who needs nothing, tops up an empty hemogenic pawn, spends nothing on one above their threshold, offers a `BS9000_TankTransfuse` job on a downed bleeding patient, never on the worker, and stops offering once empty.

Also verified statically: both assemblies build for 1.6 and 1.7, all three XML files parse, every `Class=`, `compClass`, `driverClass`, `giverClass` and `verbClass` name resolves to a type in the deployed assembly, and every referenced vanilla def exists in the game data.

### Two harness bugs found and fixed along the way

- The texture check in `BS9000GearRun` demanded `_north/_south/_east` for every path, including `Things/Item/BS9000/HemogenTank`, which is a `Graphic_Single` ground icon with no directional files. Three false FAILs. It now reads `graphicData.texPath` and `apparel.wornGraphicPath` off the defs and checks each the way the engine loads it.
- `proberun.ps1` restored the live `LP17Hotfix.dll` with a single write. The game can still hold that file after `Stop-Process`, so the write threw, the rest of the `finally` never ran, and **the probe build stayed installed in the live game**. Worse, the next run then captured that probe build as its "original" and faithfully restored it. The retry now runs 30 attempts, verifies the written length, and reports `RESTORED` / `RESTORE-FAILED` on stdout — `Write-Warning` was being swallowed when the script runs nested, which is why a failed restore looked like a clean run.

## 9. Still unverified

- Whether `CompApparelReloadable` with an `ammoDef` is only refilled by a player-ordered gizmo, or whether a vanilla work giver tops it up unprompted. The brief wants manual refill.
- Whether the engine mirrors `_east` to `_west` for worn pack graphics. The vanilla jump pack ships no `_west` file but does define separate `west` offsets.
- Exact draw offsets for the tank on the back. Copied from the jump pack, and they need an eyeball pass per body type. The probe proves the textures load, not that they sit right.
- Whether the lance texture reads well worn at the waist, and whether its warmup and range balance in a real fight.
- The full job end to end. The probe proves the work giver offers the right job on the right target; it does not drive the `JobDriver` through its toils to the patient.
- The self-refill `MapComponent`. The probe calls `HemogenTankUtility.Transfuse` directly, which is the same code path the component uses, but the 250-tick scan itself has not been observed firing.
