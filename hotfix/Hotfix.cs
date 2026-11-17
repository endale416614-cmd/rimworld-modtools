using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LP17Hotfix
{
    public class HotfixMod : Mod
    {
        // Must patch here: load-time messages play sounds before any startup hook runs, and the notices
        // below are logged by code that runs after the Mod classes exist (PWU startup, AM late-load
        // actions, the dev-mode attribute check at the end of the startup pass).
        public HotfixMod(ModContentPack content) : base(content)
        {
            SoundBeforeUi.Apply();
            PwuInfoNotices.Apply();
            MeleeAnimInfoNotices.Apply();
            ScosNoticeVideoPlayerUtil.Apply();
            PatchTargets.Apply(); // before LoadedModManager applies the XML patches
            CuisineRecipeRoles.Apply(); // same
            GppMarketValueCache.Apply(); // before the performance patch's callers are compiled with KeyOf inlined
            GppXmlCacheOps.Apply(); // before LoadedModManager applies the XML patches, while the performance patch's op sets are still consulted
            BwmSettingsNoWorld.Apply(); // before Dub's Mint Menus reads its bill templates in its late-load action
            HideFlagsNoStub.Apply(); // before Dubs Performance Analyzer's GUI setup sets the flag
            YayoToolsCompat.Apply(); // before Show Me Your Tools patches at startup
            DefOfWrongType.Apply(); // before the game binds DefOfs
            VanillaFemaleBody.Apply(); // queued behind each mod's content load, before anything can cache the body graphic
            AllsparkShortHashes.Apply(); // before Allspark Crisis generates its defs at startup, which comes before this mod's own startup hook
            BulletSoundsMeleeContext.Apply(); // before Bullet Sounds patches in the startup pass
        }
    }

    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup()
        {
            TrainsSpawnHook.Apply();
            VgeSrrUnpatch.Apply();
            SmyhKnownPatch.Apply();
            DuplicateEquippable.Apply();
            YartLayerSeparation.Apply();
            TranslationDump.Apply();
            BackCompatOfPlayer.Apply();
            VgeSpawnResume.Apply();
            CinecamSceneReset.Apply();
            CryoTankUnspawned.Apply();
            GoreFootprintType.Apply();
            MapRemovedRefs.Apply();
            VfeArtCreatorPrune.Apply();
            VaeLaunchFaction.Apply();
            WqRitualNoOutcomes.Apply();
            GiddyUpMapClose.Apply();
            FoodVarietyReset.Apply();
            GzpSowWithoutBill.Apply();
            PuahQueuedReservation.Apply();
            VefRaidGroupPrune.Apply();
            ColonyGroupUniqueId.Apply();
            SpacerShieldsDebugLine.Apply();
            LayerConnectionPostLoad.Apply();
            ClosestTileOffsetLayer.Apply();
            ReleaseDestroyedUnreserved.Apply();
            IsekaiTileNotices.Apply();
            VefHeavyWeaponUnspawned.Apply();
            ApparelSetWearRules.Apply();
            WandererJoinNewTemp.Apply();
            ArtilleryUnmannedGizmo.Apply();
            InsectGlideJob.Apply();
            VefExtinguishNoAttachments.Apply();
            HiveLookupNoMap.Apply();
            AnimeVisitorsOnly.Apply();
            DesignatorsGroupByDef.Apply();
            JobWithoutDef.Apply();
            ClassicRitualName.Apply();
            HistoryBeforeGameStart.Apply();
            MapPreviewRngCheck.Apply();
            RelationToDiscardedPawn.Apply();
            QuestDropSpotOnLand.Apply();
            IsekaiDungeonNotice.Apply();
            DoubleClickWindow.Apply();
            CompressedThingsByName.Apply();
            VeeSpaceBattleCell.Apply();
            FlareShieldGenetron.Apply();
            DispenserAnimalToils.Apply();
            GearRowHiddenFirst.Apply();
            HygieneDrawBeforeSections.Apply();
            SnowWadeHumanlike.Apply();
            HuntWithoutCastPosition.Apply();
            UnmappedPawnJobs.Apply();
            CastSourceChain.Apply();
            GrassMapComponentCache.Apply();
            GrassConcealmentInterval.Apply();
            FactionGearAmbiguousDamage.Apply();
            RenderTreeAddChildDiag.Apply();
        }
    }

    static class MapPreviewRngCheck
    {
        // Maps generate on a background thread, where RimThreaded TTR gives Rand a state per thread. Map Preview's sanity check
        // reads the main thread's counter there, so every new map reported whatever that counter had reached.
        static int replaced;

        internal static void Apply() => Lp.Run("map-preview-rng-check", () =>
        {
            MethodInfo m = AccessTools.Method("MapPreview.Patches.Patch_Verse_Map:FillComponents_Prefix");
            if (m == null) return "Map Preview not active";
            new Harmony(Lp.Id).Patch(m, transpiler: new HarmonyMethod(typeof(MapPreviewRngCheck), nameof(Transpiler)));
            return replaced == 0 ? "check not found" : null;
        });

        static Map MapToCheck() => UnityData.IsInMainThread ? MapGenerator.mapBeingGenerated : null;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            FieldInfo field = AccessTools.Field(typeof(MapGenerator), nameof(MapGenerator.mapBeingGenerated));
            foreach (CodeInstruction i in code)
            {
                if (i.LoadsField(field)) { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(MapPreviewRngCheck), nameof(MapToCheck)); replaced++; }
                yield return i;
            }
        }
    }

    static class VanillaFemaleBody
    {
        const string Package = "zx.costumes";
        const string BodiesFolder = "Things/Pawn/Humanlike/Bodies/";

        // [ZA] Apparel bundles a copy of Realistic Body's female body, which replaces the vanilla one on every pawn.
        // Its clothes stay. Lookups fall through to the game's own texture once these entries are gone.
        internal static void Apply() => LongEventHandler.ExecuteWhenFinished(() => Lp.Run("vanilla-female-body", () =>
        {
            ModContentPack mod = null;
            foreach (ModContentPack m in LoadedModManager.RunningMods)
                if (string.Equals(m.PackageIdPlayerFacing, Package, StringComparison.OrdinalIgnoreCase)) mod = m;
            if (mod == null) return Package + " not active";

            ModContentHolder<Texture2D> holder = mod.GetContentHolder<Texture2D>();
            List<string> keys = new List<string>();
            foreach (string key in holder.contentList.Keys)
                if (key.StartsWith(BodiesFolder, StringComparison.Ordinal)) keys.Add(key);
            if (keys.Count == 0) return "no body textures in " + Package;

            foreach (string key in keys)
                if (Resources.Load<Texture2D>(GenFilePaths.ContentPath<Texture2D>() + key) == null) return "no vanilla texture for " + key;

            KTrie.StringTrieSet trie = Traverse.Create(holder).Field("contentListTrie").GetValue<KTrie.StringTrieSet>();
            foreach (string key in keys)
            {
                holder.contentList.Remove(key);
                trie.Remove(key); // GetAllUnderPath reads the list through this
            }
            Lp.Note = keys.Count + " textures";
            return null;
        }));
    }

    static class ClassicRitualName
    {
        // Festival names are built from memes, the deity and the ideo adjective. A classic ideo has none, and the game's plain
        // label fallback only runs when the ideo gives no rules at all, which its leader title prevents.
        internal static void Apply() => Lp.Run("classic-ritual-name", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(RimWorld.Precept_Ritual), nameof(RimWorld.Precept_Ritual.GenerateNameRaw)), new HarmonyMethod(typeof(ClassicRitualName), nameof(Prefix)));
            return null;
        });

        static bool Prefix(RimWorld.Precept_Ritual __instance, ref string __result)
        {
            RimWorld.Ideo ideo = __instance.ideo;
            if (ideo == null || !ideo.memes.NullOrEmpty() || !ideo.adjective.NullOrEmpty() || ideo.KeyDeityName != null) return true;
            __result = __instance.def.label;
            return false;
        }
    }

    static class HistoryBeforeGameStart
    {
        // Modern Notifications dates each history entry from TicksAbs, which has no value until the game starts. VFE Tribals
        // finishes research on the starting-pawns page, so those entries logged an error. They are not worth keeping.
        internal static void Apply() => Lp.Run("history-before-game-start", () =>
        {
            MethodInfo m = AccessTools.Method("ModernNotifications.OccasionsTracker:RecordHistory");
            if (m == null) return "Modern Notifications not active";
            new Harmony(Lp.Id).Patch(m, new HarmonyMethod(typeof(HistoryBeforeGameStart), nameof(Prefix)));
            return null;
        });

        static bool Prefix() => Current.Game?.tickManager != null && Current.Game.tickManager.gameStartAbsTick != 0;
    }

    static class AnimeVisitorsOnly
    {
        // The anime style packs tag their clothes for ordinary generation (some even as royal wear), so any pawn could get them.
        // yc's Faction Editor assigns them to visitors by name, so a tag every pawn kind refuses keeps them off everyone else.
        // Visitor groups then use kinds of their own, because the kinds they used also appear in raids and settlements.
        static readonly HashSet<string> Packs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mlmlmlm.cosplayequipmentbluearchive", "mlmlmlm.cosplayapparelgenshinimpact", "mlmlmlm.cosplayapparelazurlane", "mlmlmlm.cosplayapparelhonkaistarrail",
            "mlmlmlm.cosplayapparelUmamusumePrettyDerby", "mlmlmlm.cosplayapparelprincessconnect", "mlmlmlm.cosplayapparelcounterside", "mlmlmlm.cosplayequipmentgoddessofvictorynikke",
            "mlmlmlm.cosplayapparelwutheringwaves", "mlmlmlm.cosplayapparelzenlesszonezero", "mlmlmlm.cosplayappareleternalreturn", "mlmlmlm.cosplayapparelcodevein",
            "mlmlmlm.cosplayapparelgranbluefantasy", "RF.BanGDream.MYGOStylePack", "Mihoyo.GenshinImpact.CharacterStyles", "Mihoyo.ZenlessZoneZero.CharacterStyles",
            "Kuro.WutheringWaves.CharacterStyles", "Snowbreak.ContainmentZone.CharacterStyles", "RicoFox233.NL.GFPack", "RicoFox233.PTWZZ.GFPack", "RicoFox233.PTC.GFPack",
            "RicoFox233.PTCYL.GFPack", "RicoFox233.PNC.GFYTPack", "RicoFox233.GirlsFrontline.DEFYTeam", "RicoFox233.GirlsFrontline.ARTeam", "RicoFox233.TouhouStyle.Youyoumu",
            "RicoFox233.BTR.StylePack", "RicoFox233.MIA.WhiteflutePack", "RicoFox233.yuzu.LLLJCharacterStyles", "RicoFox.apparel.MGWTPack", "RF.ATELIERSOPHIE.CharacterStyles",
            "Jupiter.EXILIUM", "Jupiter.madeinabyss", "zx.costumes", "MO.Size.Valentine.Apparel", "ponpeco.PonpecoAccessories",
        };

        static readonly HashSet<string> VisitorFactions = new HashSet<string>
        {
            "TribeCivil", "TribeRough", "TribeSavage", "TribeCannibal", "TribeRoughNeanderthal", "TribeSavageImpid", "NudistTribe", "VFEM2_CivilClan", "VFEM2_ClanRough",
            "VFEM2_ClanSavage", "VFEM2_KingdomCivil", "VFEM2_KingdomRough", "VFEM2_KingdomSavage", "VFEM2_MerchantGuild", "OutlanderCivil", "OutlanderRough", "OutlanderRoughPig",
            "VRESaurids_OutlanderRoughSaurid", "SettlerCivil", "SettlerRough", "SettlerSavage", "VFEP_Mercenaries", "TradersGuild", "GuildFaction_AdventurersGuild", "SpacerCivil", "SpacerRough",
        };

        internal static void Apply() => Lp.Run("anime-visitors-only", () =>
        {
            const string tag = "LP17_VisitorsOnly";
            PawnKindDef visitor = DefDatabase<PawnKindDef>.GetNamedSilentFail("LP17_Visitor");
            PawnKindDef guard = DefDatabase<PawnKindDef>.GetNamedSilentFail("LP17_VisitorGuard");
            PawnKindDef elite = DefDatabase<PawnKindDef>.GetNamedSilentFail("LP17_VisitorElite");
            if (visitor == null || guard == null || elite == null) return "visitor kinds not found";
            int items = 0, options = 0;
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!d.IsApparel || d.modContentPack == null || !Packs.Contains(d.modContentPack.PackageIdPlayerFacing)) continue;
                (d.apparel.tags ?? (d.apparel.tags = new List<string>())).Add(tag);
                d.apparel.tags.Remove("BestowerHood"); // kinds that require this tag ignore the refusal
                items++;
            }
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
                if (d.IsWeapon && d.modContentPack != null && Packs.Contains(d.modContentPack.PackageIdPlayerFacing)) d.weaponTags?.Clear(); // not null: Simple Sidearms caches its weapon list earlier and iterates the tags unchecked
            foreach (PawnKindDef k in DefDatabase<PawnKindDef>.AllDefs)
                (k.apparelDisallowTags ?? (k.apparelDisallowTags = new List<string>())).Add(tag);
            foreach (RimWorld.FactionDef f in DefDatabase<RimWorld.FactionDef>.AllDefs)
            {
                if (f.pawnGroupMakers == null || !VisitorFactions.Contains(f.defName)) continue;
                foreach (RimWorld.PawnGroupMaker g in f.pawnGroupMakers)
                {
                    if (g.kindDef != RimWorld.PawnGroupKindDefOf.Peaceful || g.options == null) continue;
                    foreach (RimWorld.PawnGenOption o in g.options)
                    {
                        if (o.kind?.race == null || !o.kind.RaceProps.Humanlike || o.kind.defName.Contains("Child")) continue;
                        o.kind = o.kind.combatPower < 60f ? visitor : o.kind.combatPower < 100f ? guard : elite;
                        options++;
                    }
                }
            }
            Lp.Note = items + " items, " + options + " visitor group entries";
            return null;
        });
    }

    static class DesignatorsGroupByDef
    {
        // Commands with the same label and icon count as one, and the architect menu keeps only the first. Rimefeller's linoleum
        // floors copy the labels and icons of the wood, stone tile and flagstone floor groups, so finishing its research hid those.
        internal static void Apply() => Lp.Run("designators-group-by-def", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(Command), nameof(Command.GroupsWith)), postfix: new HarmonyMethod(typeof(DesignatorsGroupByDef), nameof(Postfix)));
            return null;
        });

        static BuildableDef Placed(Gizmo g)
        {
            if (g is RimWorld.Designator_Dropdown d) g = d.Elements.Count > 0 ? d.Elements[0] : null;
            return (g as RimWorld.Designator_Build)?.PlacingDef; // Designator_Install's throws unless exactly one thing is selected
        }

        static void Postfix(Command __instance, Gizmo other, ref bool __result)
        {
            if (!__result) return;
            BuildableDef a = Placed(__instance), b = Placed(other);
            if (a != null && b != null && a != b) __result = false;
        }
    }

    static class RelationToDiscardedPawn
    {
        // An egg remembers its parents and CompHatcher.Hatch adds the relation even if a parent was discarded after laying. A discarded
        // pawn cannot be saved, so every save warned about it and the next load dropped the relation with another warning.
        internal static void Apply() => Lp.Run("relation-to-discarded-pawn", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(RimWorld.Pawn_RelationsTracker), nameof(RimWorld.Pawn_RelationsTracker.AddDirectRelation)), prefix: new HarmonyMethod(typeof(RelationToDiscardedPawn), nameof(Prefix)));
            return null;
        });

        static bool Prefix(Pawn otherPawn) => otherPawn == null || !otherPawn.Discarded;
    }

    static class QuestDropSpotOnLand
    {
        // QuestPart_DropPods picks its own random spot without the water check DropCellFinder.RandomDropSpot has, and pods do not land
        // in water, so on a coast map a spot out in the shallows made DropThingsNear warn and fall back to any walkable cell.
        internal static void Apply() => Lp.Run("quest-drop-spot-on-land", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(RimWorld.QuestPart_DropPods), "GetRandomDropSpot"), postfix: new HarmonyMethod(typeof(QuestDropSpotOnLand), nameof(Postfix)));
            return null;
        });

        static void Postfix(RimWorld.Planet.MapParent ___mapParent, ref IntVec3 __result)
        {
            Map map = ___mapParent.Map;
            if (__result.IsValid && __result.GetTerrain(map).IsWater) __result = RimWorld.DropCellFinder.RandomDropSpot(map);
        }
    }

    static class AllsparkShortHashes
    {
        // Allspark Crisis checks its generated defs' short hashes against a set of its own, so the game's set never hears of them and
        // Replace Stuff later gives its frames the same numbers, which logs a hash collision on every load.
        internal static void Apply() => Lp.Run("allspark-short-hashes", () =>
        {
            MethodInfo give = AccessTools.Method("AllsparkCrisis.BiometalDefGenerator:GiveShortHash");
            if (give == null) return "Allspark Crisis not loaded";
            new Harmony(Lp.Id).Patch(give, postfix: new HarmonyMethod(typeof(AllsparkShortHashes), nameof(Postfix)));
            return null;
        });

        static readonly FieldInfo Taken = AccessTools.Field(typeof(ShortHashGiver), "takenHashesPerDeftype");

        static void Postfix(Def __0) => ((Dictionary<Type, HashSet<ushort>>)Taken.GetValue(null))[typeof(ThingDef)].Add(__0.shortHash);
    }

    static class IsekaiDungeonNotice
    {
        static readonly MethodInfo Warning = AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) });
        static readonly MethodInfo Message = AccessTools.Method(typeof(Log), "Message", new[] { typeof(string) });

        // The room is placed and nothing overlaps: tighter spacing is the layout's normal answer to a crowded map, not a warning.
        internal static void Apply() => Lp.Run("isekai-dungeon-notice", () =>
        {
            MethodInfo place = AccessTools.Method("IsekaiDungeons.DungeonLayoutPlan:TryPlaceRoom");
            if (place == null) return "Isekai Dungeons not loaded";
            new Harmony(Lp.Id).Patch(place, transpiler: new HarmonyMethod(typeof(IsekaiDungeonNotice), nameof(Transpiler)));
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, "room placed with relaxed", Warning, Message);
    }

    static class DoubleClickWindow
    {
        // The engine counts two clicks as a double click only within 0.3 s, where Windows allows 0.5 s by default, so a double click
        // at an ordinary pace selected one thing instead of all of its kind on screen. This uses the time set in Windows.
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();

        internal static void Apply() => Lp.Run("double-click-window", () =>
        {
            MethodInfo button = AccessTools.Method("Waylith.Input.InputState:OnMouseButton");
            if (button == null) return "engine input not found";
            new Harmony(Lp.Id).Patch(button, transpiler: new HarmonyMethod(typeof(DoubleClickWindow), nameof(Transpiler)));
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            CodeInstruction window = list.Find(c => c.opcode == OpCodes.Ldc_R8 && (double)c.operand == 0.3);
            if (window == null) throw new InvalidOperationException("click window not found");
            window.operand = GetDoubleClickTime() / 1000.0;
            return list;
        }
    }

    static class JobWithoutDef
    {
        // A job whose def was missing when the game saved loads with no def, and the pawn then throws on every tick, draw and
        // thought check. Dropped here because the map is ready by now, and ending the job also frees what it reserved.
        internal static void Apply() => Lp.Run("job-without-def", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.PostMapInit)), prefix: new HarmonyMethod(typeof(JobWithoutDef), nameof(Prefix)));
            return null;
        });

        static void Prefix(Pawn __instance)
        {
            Verse.AI.Pawn_JobTracker jobs = __instance.jobs;
            if (jobs == null) return;
            for (int i = jobs.jobQueue.Count - 1; i >= 0; i--)
                if (jobs.jobQueue[i].job.def == null) jobs.jobQueue.Extract(jobs.jobQueue[i].job);
            if (jobs.curJob == null || jobs.curJob.def != null) return;
            jobs.curJob.def = RimWorld.JobDefOf.Wait; // ending a job reads its def
            jobs.EndCurrentJob(Verse.AI.JobCondition.Errored, startNewJob: false); // the tracker finds a new one on the next tick
        }
    }

    static class VefExtinguishNoAttachments
    {
        // VEF's firefoam hook reads CompAttachBase.attachments, a list the game only creates for the first attachment. Firefoam
        // threw for anything that had never burned, and the rest of that explosion cell was skipped.
        internal static void Apply() => Lp.Run("vef-extinguish-no-attachments", () =>
        {
            MethodInfo m = AccessTools.Method("VEF.Things.VanillaExpandedFramework_DamageWorker_Extinguish_Apply_Patch:Postfix");
            if (m == null) return "VEF firefoam hook not found";
            new Harmony(Lp.Id).Patch(m, new HarmonyMethod(typeof(VefExtinguishNoAttachments), nameof(Prefix)));
            return null;
        });

        static void Prefix(Thing victim)
        {
            CompAttachBase comp = victim.TryGetComp<CompAttachBase>();
            if (comp != null && comp.attachments == null) comp.attachments = new List<AttachableThing>();
        }
    }

    static class HiveLookupNoMap
    {
        // A hive lord can own a pawn that is not on a map yet, and both the game's and VFE Insectoids' hive search read its map.
        // The search threw and the lord's duties stopped there. A pawn with no map has no reachable hive, which the callers handle.
        internal static void Apply() => Lp.Run("hive-lookup-no-map", () =>
        {
            MethodInfo m = AccessTools.Method(typeof(RimWorld.LordToil_HiveRelated), "FindClosestHive");
            new Harmony(Lp.Id).Patch(m, new HarmonyMethod(typeof(HiveLookupNoMap), nameof(Prefix)) { priority = Priority.First });
            return null;
        });

        static bool Prefix(ref RimWorld.Hive __result, Pawn pawn)
        {
            if (pawn.Map != null) return true;
            __result = null;
            return false;
        }
    }

    static class YayoToolsCompat
    {
        // Show Me Your Tools enables its Yayo's Animation patches only for the Continued package id, so Yayo Animation 1.7 +
        // Tick Fix (same code and Harmony id, own package id) would run without them. Its check caches the answer in this field.
        internal static void Apply() => Lp.Run("yayo-tools-compat", () =>
        {
            FieldInfo cached = AccessTools.Field("JobEffects.YayoCompat:active");
            if (cached == null) return "Show Me Your Tools not loaded";
            if (ModLister.GetActiveModWithIdentifier("com.yayo.yayoAni.continued.briana17", true) == null) return "Yayo Animation 1.7 + Tick Fix not active";
            cached.SetValue(null, (bool?)true);
            return null;
        });
    }

    static class CuisineRecipeRoles
    {
        // Cuisine Story's recipe roles are one patch sequence. A sequence stops at its first step that finds no def, and its
        // success=Always hides that, so a list without the Wolfein, Epona and Tav mods lost every step after the second,
        // including the Vanilla Cooking recipes. Each step now runs on its own.
        internal static void Apply() => Lp.Run("cuisine-recipe-roles", () =>
        {
            FieldInfo steps = AccessTools.Field(typeof(PatchOperationSequence), "operations");
            FieldInfo success = AccessTools.Field(typeof(PatchOperation), "success");
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (!string.Equals(mod.PackageIdPlayerFacing, "rimdream.rimcuisine", StringComparison.OrdinalIgnoreCase)) continue;
                var patches = (List<PatchOperation>)mod.Patches;
                int i = patches.FindIndex(p => p is PatchOperationSequence && p.sourceFile != null && p.sourceFile.EndsWith("LowConfidenceRecipeRoles.xml"));
                if (i < 0) return "recipe role patch not found";
                var list = (List<PatchOperation>)steps.GetValue(patches[i]);
                foreach (PatchOperation step in list)
                {
                    success.SetValue(step, Enum.Parse(success.FieldType, "Always"));
                    step.sourceFile = patches[i].sourceFile;
                }
                patches.RemoveAt(i);
                patches.InsertRange(i, list);
                return null;
            }
            return "Cuisine Story not loaded";
        });
    }

    static class DefOfWrongType
    {
        // Rustspark Crisis's DefOf class passes typeof(ThingDef) instead of itself to this check, which only warns about that
        // typo. A def type is never a DefOf class, so the call has nothing to check.
        internal static void Apply() => Lp.Run("defof-wrong-type", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(RimWorld.DefOfHelper), nameof(RimWorld.DefOfHelper.EnsureInitializedInCtor)), new HarmonyMethod(typeof(DefOfWrongType), nameof(Prefix)));
            return null;
        });

        static bool Prefix(Type defOf) => !typeof(Def).IsAssignableFrom(defOf);
    }

    static class WandererJoinNewTemp
    {
        // 1.7 made RunInt call GeneratePawn_NewTemp, whose default returns null instead of falling back to GeneratePawn.
        // Mods that override only the old method (Mime, Chestburst) then get a null pawn: the quest fails and its broken
        // parts throw in every later reward quest. Overriding mods skip this base method, so only they reach the fallback.
        internal static void Apply() => Lp.Run("wanderer-join-newtemp", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(RimWorld.QuestGen.QuestNode_Root_WandererJoin), "GeneratePawn_NewTemp"), postfix: new HarmonyMethod(typeof(WandererJoinNewTemp), nameof(Postfix)));
            return null;
        });

        static void Postfix(RimWorld.QuestGen.QuestNode_Root_WandererJoin __instance, ref Pawn __result)
        {
            if (__result == null) __result = __instance.GeneratePawn();
        }
    }

    static class ArtilleryUnmannedGizmo
    {
        // CompWorldArtillery assumes its parent is a VFE Security Building_Artillery. Fortifications' Hex Mortar, Prince and
        // Emperor carry the comp on another turret class, so its gizmo throws whenever one is selected. World targeting
        // cannot work on them, so they get no gizmo.
        static Type artillery;

        internal static void Apply() => Lp.Run("artillery-unmanned-gizmo", () =>
        {
            Type t = AccessTools.TypeByName("VFESecurity.CompWorldArtillery");
            artillery = AccessTools.TypeByName("VFESecurity.Building_Artillery");
            if (t == null || artillery == null) return "VFE Security not loaded";
            new Harmony(Lp.Id).Patch(AccessTools.Method(t, "CompGetGizmosExtra"), postfix: new HarmonyMethod(typeof(ArtilleryUnmannedGizmo), nameof(Postfix)));
            return null;
        });

        static void Postfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!artillery.IsInstanceOfType(__instance.parent)) __result = Array.Empty<Gizmo>();
        }
    }

    static class InsectGlideJob
    {
        // The glide ability starts a Goto on landing with lastJobEndCondition None while the pawn still has a job, which the
        // game answers with a warning before ending that job anyway. Say the old job was interrupted.
        internal static void Apply() => Lp.Run("insect-glide-job", () =>
        {
            MethodInfo m = AccessTools.Method("VFEInsectoids.CompAbilityInsectGlide:OnJumpCompleted");
            if (m == null) return "VFE Insectoids glide not found";
            new Harmony(Lp.Id).Patch(m, transpiler: new HarmonyMethod(typeof(InsectGlideJob), nameof(Transpiler)));
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_I4_0 && list[i - 1].operand is MethodInfo mi && mi.Name == "MakeJob")
                {
                    list[i].opcode = OpCodes.Ldc_I4;
                    list[i].operand = (int)Verse.AI.JobCondition.InterruptForced;
                    return list;
                }
            }
            throw new InvalidOperationException("StartJob argument not found");
        }
    }

    static class VefHeavyWeaponUnspawned
    {
        // VEF drops a heavy weapon when the armour it needs comes off. VFE Pirates swaps a warcasket pawn's apparel during
        // generation, before the pawn has a position, so the drop fails with an error. An unspawned pawn has nowhere to drop to.
        internal static void Apply() => Lp.Run("vef-heavyweapon-unspawned", () =>
        {
            MethodInfo m = AccessTools.Method("VEF.Weapons.VanillaExpandedFramework_Pawn_ApparelTracker_Notify_ApparelRemoved_Patch:Postfix");
            if (m == null) return "VEF heavy weapon hook not found";
            new Harmony(Lp.Id).Patch(m, prefix: new HarmonyMethod(typeof(VefHeavyWeaponUnspawned), nameof(Prefix)));
            return null;
        });

        static bool Prefix(RimWorld.Pawn_ApparelTracker __0) => __0.pawn.Spawned;
    }

    static class ApparelSetWearRules
    {
        // The generator's warmth and toxic steps add clothes after checking gender only, not PawnCanWear, so a mod's wear
        // rule (Fairycore's female-only dresses) rejects them at hand-over with a warning. Drop them before that.
        internal static void Apply() => Lp.Run("apparel-set-wear-rules", () =>
        {
            MethodInfo m = AccessTools.Method("RimWorld.PawnApparelGenerator+PossibleApparelSet:GiveToPawn");
            if (m == null) return "GiveToPawn not found";
            new Harmony(Lp.Id).Patch(m, prefix: new HarmonyMethod(typeof(ApparelSetWearRules), nameof(Prefix)));
            return null;
        });

        static void Prefix(List<RimWorld.ThingStuffPair> ___aps, Pawn pawn) => ___aps.RemoveAll(p => !p.thing.apparel.PawnCanWear(pawn, true));
    }


    static class RenderTreeAddChildDiag
    {
        // Diagnostic only. A portrait redraw throws a NullReferenceException inside PawnRenderTree.AddChild, and the
        // stack frame cannot tell the Exosuit postfix from the vanilla body. Each finalizer records which one saw the
        // exception and returns it unchanged, so nothing is swallowed.
        static bool reported;

        internal static void Apply() => Lp.Run("render-tree-addchild-diag", () =>
        {
            MethodInfo vanilla = AccessTools.Method(typeof(PawnRenderTree), "AddChild");
            if (vanilla == null) return "AddChild not found";
            Harmony h = new Harmony(Lp.Id);
            h.Patch(vanilla, finalizer: new HarmonyMethod(typeof(RenderTreeAddChildDiag), nameof(VanillaFinalizer)));
            MethodInfo exosuit = AccessTools.Method("Exosuit.PawnRenderTree_Apparel_Patch:AddChildEvenHasNullParentTag");
            if (exosuit == null) { Lp.Note = "Exosuit postfix not found, vanilla only"; return null; }
            try
            {
                h.Patch(exosuit, finalizer: new HarmonyMethod(typeof(RenderTreeAddChildDiag), nameof(ExosuitFinalizer)));
                Lp.Note = "vanilla and Exosuit postfix";
            }
            catch (Exception e)
            {
                Lp.Note = "vanilla only, Exosuit postfix patch failed: " + e.GetType().Name;
            }
            return null;
        });

        static Exception VanillaFinalizer(Exception __exception, PawnRenderNode child, PawnRenderNode parent) => Report("vanilla AddChild", __exception, child, parent);

        static Exception ExosuitFinalizer(Exception __exception, PawnRenderNode child, PawnRenderNode parent) => Report("Exosuit postfix", __exception, child, parent);

        static Exception Report(string source, Exception e, PawnRenderNode child, PawnRenderNode parent)
        {
            if (e == null || reported) return e;
            reported = true;
            try
            {
                ThingDef def = child?.apparel?.def;
                Log.Message("[LP17 Hotfix] render-tree-addchild-diag: " + source + " finalizer saw " + e.GetType().Name
                    + "; child null=" + (child == null)
                    + ", child.Props=" + (child == null ? "n/a" : child.Props == null ? "null" : "set")
                    + ", child.Props.parentTagDef=" + (child?.Props == null ? "none" : DefName(child.Props.parentTagDef))
                    + ", child.apparel.def=" + DefName(def)
                    + ", child.apparel.def.apparel=" + (def == null ? "n/a" : def.apparel == null ? "null" : "set")
                    + ", parent null=" + (parent == null)
                    + ", parent.Props=" + (parent == null ? "n/a" : parent.Props == null ? "null" : "set")
                    + ", parent.Props.tagDef=" + (parent?.Props == null ? "none" : DefName(parent.Props.tagDef))
                    + ", node type=" + (child == null ? "null" : child.GetType().Name));
            }
            catch (Exception x)
            {
                Log.Message("[LP17 Hotfix] render-tree-addchild-diag: diagnostic itself failed: " + x.GetType().Name);
            }
            return e;
        }

        static string DefName(Def d) => d == null ? "none" : d.defName;
    }
    static class IsekaiTileNotices
    {
        static readonly MethodInfo Warning = AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) });
        static readonly MethodInfo Message = AccessTools.Method(typeof(Log), "Message", new[] { typeof(string) });

        // A colony whose tile has no land reachable overland has no site for these quests: the addon drops the
        // quest and the hunt uses the home map, as both mods intend. That is a normal outcome, not a warning.
        internal static void Apply() => Lp.Run("isekai-tile-notices", () =>
        {
            MethodInfo quest = AccessTools.Method("IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest:BuildAndPostQuest");
            MethodInfo hunt = AccessTools.Method("IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt:CreateHuntQuest");
            if (quest == null && hunt == null) return "Isekai mods not loaded";
            Harmony h = new Harmony(Lp.Id);
            if (quest != null) h.Patch(quest, transpiler: new HarmonyMethod(typeof(IsekaiTileNotices), nameof(Quest)));
            if (hunt != null) h.Patch(hunt, transpiler: new HarmonyMethod(typeof(IsekaiTileNotices), nameof(Hunt)));
            return null;
        });

        static IEnumerable<CodeInstruction> Quest(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, "couldn't find a target tile", Warning, Message);

        static IEnumerable<CodeInstruction> Hunt(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, "Could not find suitable tile", Warning, Message);
    }

    static class BwmSettingsNoWorld
    {
        // Better Workbench Management keeps its bill data in a world component, but Dub's Mint Menus scribes
        // Bill_Production from mod settings at the main menu, where no world exists; the throw lost every template.
        internal static void Apply() => Lp.Run("bwm-settings-no-world", () =>
        {
            MethodInfo postfix = AccessTools.Method("ImprovedWorkbenches.ExtendedBillData_ExposeData:Postfix");
            if (postfix == null) return "Better Workbench Management not loaded";
            new Harmony(Lp.Id).Patch(postfix, prefix: new HarmonyMethod(typeof(BwmSettingsNoWorld), nameof(Prefix)));
            return null;
        });

        static bool Prefix() => Current.Game?.World != null;
    }

    static class HideFlagsNoStub
    {
        // The setter only logs that it is a stub; the engine has no hide flags, so skipping it changes nothing.
        internal static void Apply() => Lp.Run("hideflags-no-stub", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.PropertySetter(typeof(UnityEngine.Object), "hideFlags"), prefix: new HarmonyMethod(typeof(HideFlagsNoStub), nameof(Prefix)));
            return null;
        });

        static bool Prefix() => false;
    }

    static class PuahQueuedReservation
    {
        // The game passes errorOnFailed=false for queued jobs, whose target may be taken by the time they start.
        // Pick Up And Haul ignores the argument and always logs, though the job then just ends quietly.
        internal static void Apply() => Lp.Run("puah-queued-reservation", () =>
        {
            MethodInfo m = AccessTools.Method("PickUpAndHaul.JobDriver_HaulToInventory:TryMakePreToilReservations");
            if (m == null) return "Pick Up And Haul not loaded";
            new Harmony(Lp.Id).Patch(m, transpiler: new HarmonyMethod(typeof(PuahQueuedReservation), nameof(Transpiler)));
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            int done = 0;
            for (int i = 2; i < list.Count; i++)
            {
                // Reserve(claimant, target, job, maxPawns, stackCount, layer, errorOnFailed, ignoreOtherReservations)
                if (list[i].operand is MethodInfo mi && mi.Name == "Reserve" && mi.DeclaringType.Name == "ReservationUtility" && list[i - 2].opcode == OpCodes.Ldc_I4_1)
                {
                    list[i - 2].opcode = OpCodes.Ldarg_1;
                    list[i - 2].operand = null;
                    done++;
                }
            }
            if (done != 2) throw new InvalidOperationException("expected 2 Reserve calls, found " + done);
            return list;
        }
    }

    static class VefRaidGroupPrune
    {
        // RaidGroup keeps its lords and faction by reference and never drops them, so once a raid ends the save
        // points at objects that are gone: a warning on saving and an error on every load of that save.
        internal static void Apply() => Lp.Run("vef-raidgroup-prune", () =>
        {
            Type t = AccessTools.TypeByName("VEF.Storyteller.RaidGroup");
            if (t == null) return "RaidGroup not found";
            new Harmony(Lp.Id).Patch(AccessTools.Method(t, "ExposeData"), prefix: new HarmonyMethod(typeof(VefRaidGroupPrune), nameof(Prefix)));
            return null;
        });

        static void Prefix(HashSet<Pawn> ___pawns, HashSet<Verse.AI.Group.Lord> ___lords, ref RimWorld.Faction ___faction)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;
            ___pawns?.RemoveWhere(p => p == null || p.Discarded);
            ___lords?.RemoveWhere(l => l?.lordManager == null || !Find.Maps.Contains(l.lordManager.map) || !l.lordManager.lords.Contains(l));
            if (___faction != null && !Find.FactionManager.AllFactionsListForReading.Contains(___faction)) ___faction = null;
        }
    }

    static class ColonyGroupUniqueId
    {
        static FieldInfo groupId, colonyGroups;
        static MethodInfo manager;

        // Colony Groups numbers a group from a count of the current ones, but its load ID is that number alone,
        // so a group made after a map closed, or a task force next to a colony, can repeat an ID.
        internal static void Apply() => Lp.Run("colony-group-unique-id", () =>
        {
            Type t = AccessTools.TypeByName("TacticalGroups.ColonyGroup");
            if (t == null) return "Colony Groups not loaded";
            groupId = AccessTools.Field(t, "groupID");
            manager = AccessTools.PropertyGetter(AccessTools.TypeByName("TacticalGroups.TacticUtils"), "TacticalGroups");
            colonyGroups = AccessTools.Field(manager.ReturnType, "colonyGroups");
            if (groupId == null || colonyGroups == null) return "group fields not found";
            Harmony h = new Harmony(Lp.Id);
            HarmonyMethod fix = new HarmonyMethod(typeof(ColonyGroupUniqueId), nameof(Fix));
            foreach (ConstructorInfo c in AccessTools.GetDeclaredConstructors(t))
                if (c.GetParameters().Length > 0) h.Patch(c, postfix: fix); // the empty one is the load path, which keeps the saved ID
            h.Patch(AccessTools.Method(t, "ConvertToTaskForce"), postfix: fix);
            h.Patch(AccessTools.Method(t, "ConvertToColonyGroup"), postfix: fix);
            return null;
        });

        internal static void Fix(object __instance)
        {
            int id = (int)groupId.GetValue(__instance), max = 0;
            bool clash = false;
            foreach (object g in ((System.Collections.IDictionary)colonyGroups.GetValue(manager.Invoke(null, null))).Values)
            {
                if (g == __instance) continue;
                int other = (int)groupId.GetValue(g);
                max = Math.Max(max, other);
                clash |= other == id;
            }
            if (clash) groupId.SetValue(__instance, max + 1);
        }
    }

    static class SpacerShieldsDebugLine
    {
        // A leftover debug line: the mod logs each flyer's def label as a warning on landing.
        internal static void Apply() => Lp.Run("spacer-shields-debug-line", () =>
        {
            MethodInfo m = AccessTools.Method("LTS_SpacerShields.PawnFlyer_LandingEffects_Patch:PawnFlyer_LandingEffects_Postfix");
            if (m == null) return "Spacer Shields not loaded";
            new Harmony(Lp.Id).Patch(m, transpiler: new HarmonyMethod(typeof(SpacerShieldsDebugLine), nameof(Transpiler)));
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo warning = AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) });
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            CodeInstruction call = list.Find(c => c.operand as MethodInfo == warning);
            if (call == null) throw new InvalidOperationException("Log.Warning call not found");
            call.opcode = OpCodes.Pop;
            call.operand = null;
            return list;
        }
    }

    static class ClosestTileOffsetLayer
    {
        // The closest-tile query keeps only tiles in the origin tile's reachability field, so from the surface no tile of
        // an off-centre layer (Desolate Moon's moon, origin 50,0,-200) qualifies. Gravship targeting then measures every
        // moon tile as unreachable and rejects it as beyond range. Fall back to the nearest tile by position.
        internal static void Apply() => Lp.Run("closest-tile-offset-layer", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(PlanetLayer), nameof(PlanetLayer.GetClosestTile_NewTemp)), postfix: new HarmonyMethod(typeof(ClosestTileOffsetLayer), nameof(Postfix)));
            return null;
        });

        static void Postfix(PlanetLayer __instance, PlanetTile other, ref PlanetTile __result)
        {
            if (__result.Valid || !other.Valid || other.Layer == __instance || __instance.TilesCount == 0) return;
            Vector3 from = other.Layer.GetTileCenter(other.tileId);
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < __instance.TilesCount; i++)
            {
                float d = (__instance.GetTileCenter(i) - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            __result = new PlanetTile(best, __instance);
        }
    }

    static class ReleaseDestroyedUnreserved
    {
        // Releasing a destroyed thing that holds no matching reservation still reaches the "remove" branch and passes a null
        // reservation to MapEvents.Notify_ReservationRemoved, which throws. Build From Inventory's drop toil releases wood
        // that a frame or the generator already used up, so each such haul logged a warning and an exception.
        internal static void Apply() => Lp.Run("release-destroyed-unreserved", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(Verse.AI.ReservationManager), nameof(Verse.AI.ReservationManager.Release)), prefix: new HarmonyMethod(typeof(ReleaseDestroyedUnreserved), nameof(Prefix)) { priority = Priority.First });
            return null;
        });

        static bool Prefix(LocalTargetInfo target, Pawn claimant, Verse.AI.Job job, List<Verse.AI.ReservationManager.Reservation> ___reservations)
        {
            if (!target.ThingDestroyed) return true;
            foreach (var r in ___reservations)
                if (r.Target == target && r.Claimant == claimant && r.Job == job) return true;
            return false;
        }
    }

    static class LayerConnectionPostLoad
    {
        // Two scenario layers can hold equal connections (same tag, cost, zoom), and the post-load set rejects the
        // second with a warning. A connection scribes only plain values, so its post-load pass does nothing.
        internal static void Apply() => Lp.Run("layerconnection-postload", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(PostLoadIniter), "RegisterForPostLoadInit"), prefix: new HarmonyMethod(typeof(LayerConnectionPostLoad), nameof(Prefix)));
            return null;
        });

        static bool Prefix(IExposable s) => !(s is RimWorld.LayerConnection);
    }

    static class CompressedThingsByName
    {
        // Rock, ore and other compressed things are saved as one short hash per map cell and nothing else. The game derives the
        // hash from the def name and steps past values already taken, in name order, so a mod that adds one def can move another
        // def to the next value. A map saved before that then names the wrong def: this colony's 50 silver ore cells (21442)
        // pointed at a door frame def and were dropped on load. Save the def name of every value in use next to the map data and
        // move the data to the current values on load. A save from before this fix has no names; there a value that now belongs
        // to a non-compressible def goes to the one compressible def whose own hash sits at or just below it and which now sits above it.
        const string Label = "lp17CompressedThingDefs";
        const int GuessWindow = 8;
        static readonly MethodInfo RenamedDef = AccessTools.Method(typeof(BackCompatibility), "BackCompatibleDefName"); // by reflection: its XmlNode parameter type is not referenced
        static readonly AccessTools.FieldRef<MapFileCompressor, byte[]> Data = AccessTools.FieldRefAccess<MapFileCompressor, byte[]>("compressedData");

        internal static void Apply() => Lp.Run("compressed-things-by-name", () =>
        {
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(MapFileCompressor), nameof(MapFileCompressor.ExposeData)), postfix: new HarmonyMethod(typeof(CompressedThingsByName), nameof(Postfix)));
            return null;
        });

        static void Postfix(MapFileCompressor __instance)
        {
            try
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    List<string> names = NamesIn(Data(__instance), ByHash());
                    Scribe_Collections.Look(ref names, Label, LookMode.Value);
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    List<string> names = null;
                    Scribe_Collections.Look(ref names, Label, LookMode.Value);
                    byte[] data = Data(__instance);
                    if (data == null) return;
                    Dictionary<ushort, ThingDef> byHash = ByHash();
                    Dictionary<ushort, ushort> moves = names != null ? MovesFromNames(names) : MovesByGuess(data, byHash);
                    int cells = Translate(data, moves);
                    if (cells > 0) Log.Message("[LP17 Hotfix] compressed-things-by-name: moved " + cells + " cells to current short hashes (" + Describe(moves, byHash) + (names != null ? ")" : "; no names in this save, matched by def name hash)"));
                }
            }
            catch (Exception e)
            {
                // Without this fix the game saves and loads as it always did; an error here must not cost the save.
                Log.Error("[LP17 Hotfix] compressed-things-by-name failed, map data left as the game wrote it: " + e);
            }
        }

        // The game's loader resolves a value the same way: the first def in database order wins. DefDatabase's own lookup
        // table is built once at startup and misses defs that mods add later, such as Replace Stuff's frames.
        static Dictionary<ushort, ThingDef> ByHash()
        {
            Dictionary<ushort, ThingDef> byHash = new Dictionary<ushort, ThingDef>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.shortHash != 0 && !byHash.ContainsKey(def.shortHash)) byHash.Add(def.shortHash, def);
            return byHash;
        }

        internal static List<string> NamesIn(byte[] data, Dictionary<ushort, ThingDef> byHash)
        {
            List<string> names = new List<string>();
            if (data == null) return names;
            HashSet<ushort> seen = new HashSet<ushort>();
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                ushort value = (ushort)(data[i] | (data[i + 1] << 8));
                if (value == 0 || !seen.Add(value)) continue;
                if (byHash.TryGetValue(value, out ThingDef def)) names.Add(value + "=" + def.defName);
            }
            return names;
        }

        internal static Dictionary<ushort, ushort> MovesFromNames(List<string> names)
        {
            Dictionary<ushort, ushort> moves = new Dictionary<ushort, ushort>();
            foreach (string entry in names)
            {
                int cut = entry == null ? -1 : entry.IndexOf('=');
                if (cut <= 0 || !ushort.TryParse(entry.Substring(0, cut), out ushort saved)) continue;
                string name = entry.Substring(cut + 1);
                ThingDef def = Named(name);
                if (def != null && def.shortHash != 0 && def.shortHash != saved) moves[saved] = def.shortHash;
            }
            return moves;
        }

        static ThingDef Named(string name)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if (def != null || RenamedDef == null) return def;
            string renamed = RenamedDef.Invoke(null, new object[] { typeof(ThingDef), name, false, null }) as string; // renamed since the save
            return renamed == null || renamed == name ? null : DefDatabase<ThingDef>.GetNamedSilentFail(renamed);
        }

        static Dictionary<ushort, ushort> MovesByGuess(byte[] data, Dictionary<ushort, ThingDef> byHash)
        {
            Dictionary<ushort, ushort> moves = new Dictionary<ushort, ushort>();
            List<ThingDef> compressible = null;
            HashSet<ushort> seen = new HashSet<ushort>();
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                ushort value = (ushort)(data[i] | (data[i + 1] << 8));
                if (value == 0 || !seen.Add(value)) continue;
                if (!byHash.TryGetValue(value, out ThingDef now) || now.saveCompressible) continue; // a value with no def stays with the game's own back-compat path
                if (BackCompatibility.BackCompatibleThingDefWithShortHash_Force(value, ScribeMetaHeaderUtility.loadedGameVersionMajor, ScribeMetaHeaderUtility.loadedGameVersionMinor) != null) continue;
                if (compressible == null) compressible = DefDatabase<ThingDef>.AllDefsListForReading.FindAll(d => d.saveCompressible);
                ThingDef match = null;
                int matches = 0;
                foreach (ThingDef def in compressible)
                {
                    int gap = value - (ushort)(GenText.StableStringHash(def.defName) % 65535);
                    if (gap >= 0 && gap <= GuessWindow && def.shortHash > value) { match = def; matches++; } // a def pushed off this value now sits above it
                }
                if (matches == 1) moves[value] = match.shortHash;
            }
            return moves;
        }

        internal static int Translate(byte[] data, Dictionary<ushort, ushort> moves)
        {
            if (moves.Count == 0) return 0;
            int cells = 0;
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                if (!moves.TryGetValue((ushort)(data[i] | (data[i + 1] << 8)), out ushort to)) continue;
                data[i] = (byte)(to & 0xFF);
                data[i + 1] = (byte)(to >> 8);
                cells++;
            }
            return cells;
        }

        static string Describe(Dictionary<ushort, ushort> moves, Dictionary<ushort, ThingDef> byHash)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<ushort, ushort> move in moves) parts.Add(move.Key + " -> " + move.Value + " " + (byHash.TryGetValue(move.Value, out ThingDef def) ? def.defName : "?"));
            return string.Join(", ", parts);
        }

        // Two defs whose saved values chain (one def's old value is the other's current value) must both land right.
        internal static string SelfTest()
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading.FindAll(d => d.saveCompressible && d.shortHash != 0);
            if (defs.Count < 2) return "fewer than two compressible defs";
            ThingDef a = defs[0], b = defs[1];
            Dictionary<ushort, ThingDef> byHash = ByHash();
            ushort free = 1;
            while (byHash.ContainsKey(free)) free++;
            ushort[] saved = { free, a.shortHash, 0, free };
            byte[] data = new byte[saved.Length * 2];
            for (int i = 0; i < saved.Length; i++) { data[i * 2] = (byte)(saved[i] & 0xFF); data[i * 2 + 1] = (byte)(saved[i] >> 8); }
            int cells = Translate(data, MovesFromNames(new List<string> { free + "=" + a.defName, a.shortHash + "=" + b.defName, "bad", "7=NoSuchDef" }));
            ushort[] want = { a.shortHash, b.shortHash, 0, a.shortHash };
            for (int i = 0; i < want.Length; i++)
                if ((ushort)(data[i * 2] | (data[i * 2 + 1] << 8)) != want[i]) return "cell " + i + " is " + (data[i * 2] | (data[i * 2 + 1] << 8)) + ", want " + want[i];
            if (cells != 3) return "moved " + cells + " cells, want 3";
            List<string> names = NamesIn(data, byHash);
            if (!names.Contains(a.shortHash + "=" + a.defName) || !names.Contains(b.shortHash + "=" + b.defName)) return "names list is missing an entry";
            return null;
        }
    }

    static class BulletSoundsMeleeContext
    {
        // Bullet Sounds patches Verb_MeleeAttack.ApplyMeleeDamageToTarget, which is abstract, so Harmony refuses and the mod logs
        // two warnings on every launch. Its cast patch already covers the vanilla call; put this prefix and finalizer on every
        // override so direct calls are covered too, and tell its own attempt there is nothing to patch.
        internal static void Apply() => Lp.Run("bulletsounds-melee-context", () =>
        {
            Type patch = AccessTools.TypeByName("Senilia.BulletSounds.Verb_MeleeAttack_ApplyDamage_Context");
            if (patch == null) return "Bullet Sounds not loaded";
            MethodInfo prepare = AccessTools.Method(patch, "Prepare"), prefix = AccessTools.Method(patch, "Prefix"), finalizer = AccessTools.Method(patch, "Finalizer");
            if (prepare == null || prefix == null || finalizer == null) return "patch class changed";
            MethodInfo target = AccessTools.Method(typeof(RimWorld.Verb_MeleeAttack), "ApplyMeleeDamageToTarget");
            if (target == null || !target.IsAbstract) return "target is not abstract, the mod can patch it itself";
            Harmony harmony = new Harmony(Lp.Id);
            int count = 0;
            foreach (Type type in typeof(RimWorld.Verb_MeleeAttack).AllSubclasses())
            {
                if (type.ContainsGenericParameters) continue; // an open generic type has no method to patch
                MethodInfo over =AccessTools.DeclaredMethod(type, "ApplyMeleeDamageToTarget");
                if (over == null || over.IsAbstract) continue;
                harmony.Patch(over, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                count++;
            }
            if (count == 0) return "no override found";
            harmony.Patch(prepare, postfix: new HarmonyMethod(typeof(BulletSoundsMeleeContext), nameof(NothingToPatch)));
            Lp.Note = count + " overrides";
            return null;
        });

        static void NothingToPatch(ref bool __result) => __result = false;
    }

    static class VeeSpaceBattleCell
    {
        // The space battle event ignores a failed search for a landing cell and spawns its 2x2 ship chunk at whatever cell the
        // search last tried. With no trade ship the search centre is the map corner, so on a coast map each drop logged "Tried to
        // spawn ... out of bounds" and the chunk was lost. A failed or non-fitting result gets a cell from a search of the whole
        // map outside the home area; if that finds nothing the event behaves as before.
        internal static void Apply() => Lp.Run("vee-spacebattle-cell", () =>
        {
            MethodInfo find = AccessTools.Method("VEE.RegularEvents.SpaceBattle:VEETryFindSkyfallerCell");
            if (find == null) return "Vanilla Events Expanded not loaded";
            new Harmony(Lp.Id).Patch(find, postfix: new HarmonyMethod(typeof(VeeSpaceBattleCell), nameof(Postfix)));
            return null;
        });

        static void Postfix(ThingDef skyfaller, Map map, ref IntVec3 pos, ref bool __result)
        {
            if (__result && Fits(skyfaller, pos, map)) return;
            // Away from the home area and from colonists: one of the chunks explodes when it lands.
            __result = CellFinderLoose.TryFindSkyfallerCell(skyfaller, map, RimWorld.TerrainAffordanceDefOf.Walkable, out IntVec3 found, 10, default(IntVec3), -1, false, true, true, false, true, false, c => c.InBounds(map) && !c.Fogged(map) && c.Walkable(map) && !map.areaManager.Home[c]) && Fits(skyfaller, found, map);
            if (__result) pos = found;
        }

        static bool Fits(ThingDef skyfaller, IntVec3 cell, Map map) => cell.IsValid && GenAdj.OccupiedRect(cell, Rot4.North, skyfaller.size).InBounds(map);
    }

    static class FlareShieldGenetron
    {
        // During a solar flare VQE The Generator charges batteries straight from Genetrons that work in flares, because the
        // net itself is down. RT Solar Flare Shield keeps the net running under a shield, where the Genetron's output already
        // counts, so with a shield up the batteries were charged twice. The battery path now runs only when RT also says the
        // net is down.
        static Func<RimWorld.GameConditionManager, Map, bool> disabled;

        internal static void Apply() => Lp.Run("flare-shield-genetron", () =>
        {
            MethodInfo rt = AccessTools.Method("RT_SolarFlareShield.Patch_PowerNetTick:ElectricityDisabled");
            if (rt == null) return "RT Solar Flare Shield not loaded";
            MethodInfo batteries = AccessTools.Method("VanillaQuestsExpandedTheGenerator.VanillaQuestsExpandedTheGenerator_PowerNet_PowerNetTick_Patch:PowerBatteries");
            if (batteries == null) return "VQE The Generator not loaded";
            disabled = AccessTools.MethodDelegate<Func<RimWorld.GameConditionManager, Map, bool>>(rt);
            new Harmony(Lp.Id).Patch(batteries, prefix: new HarmonyMethod(typeof(FlareShieldGenetron), nameof(Batteries)));
            return null;
        });

        // The patched method is another mod's postfix, a static method, so its argument is read by position.
        internal static bool Batteries(RimWorld.PowerNet __0) => disabled(__0.Map.gameConditionManager, __0.Map);
    }

    static class DispenserAnimalToils
    {
        // Animals Logic gives animals and wild men their own toils for a nutrient paste dispenser. Common Sense's prefix on the
        // same method runs first and, with its "clean before eating" option on, replaces the toils for every pawn and skips the
        // rest. Ask Animals Logic first.
        delegate bool AnimalToils(ref RimWorld.JobDriver_Ingest driver, ref IEnumerable<Verse.AI.Toil> result);
        static AnimalToils animalToils;

        internal static void Apply() => Lp.Run("dispenser-animal-toils", () =>
        {
            MethodInfo theirs = AccessTools.Method("AnimalsLogic.AnimalsUseDispenser:PrepareToIngestToils_DispenserPrefix");
            if (theirs == null) return "Animals Logic not loaded";
            MethodInfo target = AccessTools.Method(typeof(RimWorld.JobDriver_Ingest), "PrepareToIngestToils_Dispenser");
            if (target == null) return "JobDriver_Ingest.PrepareToIngestToils_Dispenser not found";
            animalToils = AccessTools.MethodDelegate<AnimalToils>(theirs);
            new Harmony(Lp.Id).Patch(target, prefix: new HarmonyMethod(typeof(DispenserAnimalToils), nameof(Prefix)) { priority = Priority.First });
            return null;
        });

        static bool Prefix(ref RimWorld.JobDriver_Ingest __instance, ref IEnumerable<Verse.AI.Toil> __result) => animalToils(ref __instance, ref __result);
    }

    static class GearRowHiddenFirst
    {
        // Belt Lantern and Exosuit Framework hide a gear tab row by skipping DrawThingRow. Common Sense's prefix on that method
        // runs before theirs and has by then drawn its unload button for the hidden thing, which is left on top of the next
        // row's button and takes its clicks. Ask the hiding prefixes first.
        static readonly List<Func<Thing, bool>> shows = new List<Func<Thing, bool>>();

        internal static void Apply() => Lp.Run("gear-row-hidden-first", () =>
        {
            foreach (string name in new[] { "LanternMod.LanternGearDuplicateRowPatch:Prefix", "Exosuit.ITab_Pawn_Gear_DrawThingRow:DrawThingRow" })
            {
                MethodInfo hide = AccessTools.Method(name);
                if (hide != null) shows.Add(AccessTools.MethodDelegate<Func<Thing, bool>>(hide));
            }
            if (shows.Count == 0) return "no row-hiding mod loaded";
            MethodInfo target = AccessTools.Method(typeof(RimWorld.ITab_Pawn_Gear), "DrawThingRow");
            if (target == null) return "ITab_Pawn_Gear.DrawThingRow not found";
            new Harmony(Lp.Id).Patch(target, prefix: new HarmonyMethod(typeof(GearRowHiddenFirst), nameof(Prefix)) { priority = Priority.First });
            Lp.Note = shows.Count + " hiding prefixes";
            return null;
        });

        internal static bool Prefix(Thing thing)
        {
            foreach (Func<Thing, bool> show in shows)
                if (!show(thing)) return false;
            return true;
        }
    }

    static class UnmappedPawnJobs
    {
        // The game ticks a pawn's job tracker whenever the pawn is not a world pawn, without checking that it is on a map, and
        // the think nodes it then runs read Pawn.MapHeld. One animal reached that state off the map: JobGiver_AnimalFlee,
        // RestUtility.FindBedFor, JobGiver_ExitMap and JobDriver_Wait's own first toil all threw in the same tick, and the game
        // gave up and left the pawn jobless. Such a pawn has nothing to decide until it is on a map again, so it is not ticked.
        // Its job is left alone, because ending one runs cleanup that reads the map as well.
        static bool reported;

        internal static void Apply() => Lp.Run("unmapped-pawn-jobs", () =>
        {
            Harmony harmony = new Harmony(Lp.Id);
            HarmonyMethod prefix = new HarmonyMethod(typeof(UnmappedPawnJobs), nameof(Prefix));
            int patched = 0;
            foreach (string name in new[] { "JobTrackerTick", "JobTrackerTickInterval" })
            {
                MethodInfo tick = AccessTools.Method(typeof(Verse.AI.Pawn_JobTracker), name);
                if (tick == null) continue;
                harmony.Patch(tick, prefix: prefix);
                patched++;
            }
            if (patched == 0) return "no Pawn_JobTracker tick method found";
            Lp.Note = patched + " tick methods";
            return null;
        });

        static bool Prefix(Pawn ___pawn)
        {
            if (___pawn == null || ___pawn.MapHeld != null) return true;
            if (!reported)
            {
                reported = true;
                Log.Message("[LP17 Hotfix] unmapped-pawn-jobs: " + ___pawn.ToStringSafe() + " was job-ticked while off the map; its job ticks are skipped until it is on one.");
            }
            return false;
        }
    }

    static class HygieneDrawBeforeSections
    {
        // Dubs Bad Hygiene refreshes its overlay sections from HygienePipeMapComp.FinalizeInit, through a static pass over every
        // map's hygiene component. Map.mapDrawer builds its section array in an action queued for after FinalizeInit, so the map
        // being generated has none yet and GridLayer.RefreshSections read a null array; the game logged that and the rest of the
        // pass was skipped. The game's own MapDrawer.MapMeshDirty returns early on the same null. The dirty flag is left set
        // here, so the refresh still happens on the first frame that draws the map.
        //
        // The pass only runs for two maps on one world tile, and a pocket map's tile is invalid, which reads as the same tile
        // for all of them: entering a dungeon with another pocket map already open also merged their plumbing into one net.
        // Pocket maps share no tile, so the pass is skipped for them.
        static AccessTools.FieldRef<object, Map> gridMap;
        static AccessTools.FieldRef<MapDrawer, Section[,]> drawerSections;

        internal static void Apply() => Lp.Run("hygiene-draw-before-sections", () =>
        {
            MethodInfo refresh = AccessTools.Method("DubsBadHygiene.GridLayer:RefreshSections");
            if (refresh == null) return "Dubs Bad Hygiene not loaded";
            MethodInfo onTile = AccessTools.Method("DubsBadHygiene.HygienePipeMapComp:RefreshInternetsOnTile");
            if (onTile == null) return "HygienePipeMapComp.RefreshInternetsOnTile not found";
            gridMap = AccessTools.FieldRefAccess<Map>(refresh.DeclaringType, "map");
            drawerSections = AccessTools.FieldRefAccess<MapDrawer, Section[,]>("sections");
            Harmony harmony = new Harmony(Lp.Id);
            harmony.Patch(refresh, prefix: new HarmonyMethod(typeof(HygieneDrawBeforeSections), nameof(RefreshPrefix)));
            harmony.Patch(onTile, prefix: new HarmonyMethod(typeof(HygieneDrawBeforeSections), nameof(TilePrefix)));
            return null;
        });

        // Runs for every visible section of every frame, so both field reads are cached delegates.
        static bool RefreshPrefix(object __instance, ref bool __result)
        {
            Map map = gridMap(__instance);
            if (map != null && map.mapDrawer != null && drawerSections(map.mapDrawer) != null) return true;
            __result = false;
            return false;
        }

        // The method is static, so its argument is read by position. Every real world tile is zero or above.
        static bool TilePrefix(int __0) => __0 >= 0;
    }

    static class SnowWadeHumanlike
    {
        // Snow Perfected sizes its snow puffs from Pawn_AgeTracker.CurKindLifeStage and only then checks whether the pawn is
        // humanlike; the game's own getter logs an error for a humanlike pawn. Such a pawn takes that method's 1f branch
        // anyway, so the check runs first here.
        internal static void Apply() => Lp.Run("snow-wade-humanlike", () =>
        {
            MethodInfo size = AccessTools.Method("DynamicSnow.SnowWade:Size");
            if (size == null) return "Snow Perfected not loaded";
            new Harmony(Lp.Id).Patch(size, prefix: new HarmonyMethod(typeof(SnowWadeHumanlike), nameof(Prefix)));
            return null;
        });

        // Size is static, so its argument is read by position.
        static bool Prefix(Pawn __0, ref float __result)
        {
            if (__0 == null || __0.RaceProps == null || !__0.RaceProps.Humanlike) return true;
            __result = 1f;
            return false;
        }
    }

    static class HuntWithoutCastPosition
    {
        // WorkGiver_HunterHunt.HasJobOnThing proves a cast position exists before it offers the job, and
        // Toils_Combat.GotoCastPosition searches again when the job starts. For some targets the two searches disagree, and
        // then the toil ends the job as Incompletable and the work giver offers the same animal again in the same tick, until
        // the game gives the pawn an error recovery job ("started 10 jobs in one tick"). A target the driver has just failed on
        // is refused until it moves, which for an animal is almost at once, or until the search is worth repeating.
        const int Forget = 2500; // about 40 seconds at normal speed: long enough for a pawn or a fire in the way to clear
        static readonly Dictionary<long, Pair<IntVec3, int>> refused = new Dictionary<long, Pair<IntVec3, int>>();
        static AccessTools.FieldRef<Verse.AI.Pawn_JobTracker, Pawn> trackerPawn;

        internal static void Apply() => Lp.Run("hunt-without-cast-position", () =>
        {
            MethodInfo end = AccessTools.Method(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.EndCurrentJob));
            if (end == null) return "Pawn_JobTracker.EndCurrentJob not found";
            MethodInfo has = AccessTools.Method(typeof(RimWorld.WorkGiver_HunterHunt), nameof(RimWorld.WorkGiver_HunterHunt.HasJobOnThing));
            if (has == null) return "WorkGiver_HunterHunt.HasJobOnThing not found";
            string wrong = SelfCheck();
            if (wrong != null) return wrong;
            trackerPawn = AccessTools.FieldRefAccess<Verse.AI.Pawn_JobTracker, Pawn>("pawn");
            Harmony harmony = new Harmony(Lp.Id);
            harmony.Patch(end, prefix: new HarmonyMethod(typeof(HuntWithoutCastPosition), nameof(EndPrefix)));
            harmony.Patch(has, postfix: new HarmonyMethod(typeof(HuntWithoutCastPosition), nameof(HasPostfix)));
            return null;
        });

        static long Key(int hunter, int victim) => ((long)hunter << 32) | (uint)victim;

        static bool StillRefused(IntVec3 at, Pair<IntVec3, int> failed, int now) => failed.First == at && now - failed.Second < Forget;

        // The key packs two thing ids into one long, and an entry expires when the target moves or when it is old enough, so
        // both are checked here rather than in a game.
        static string SelfCheck()
        {
            if (Key(1, 2) == Key(2, 1) || Key(3, 4) == Key(3, 5) || Key(4, 3) == Key(5, 3)) return "key packing collides";
            IntVec3 cell = new IntVec3(10, 0, 20), moved = new IntVec3(11, 0, 20);
            Pair<IntVec3, int> failed = new Pair<IntVec3, int>(cell, 1000);
            if (!StillRefused(cell, failed, 1000 + Forget - 1)) return "a target that has not moved was let through";
            if (StillRefused(cell, failed, 1000 + Forget)) return "a stale refusal was kept";
            if (StillRefused(moved, failed, 1000)) return "a target that moved was still refused";
            return null;
        }

        static void EndPrefix(Verse.AI.Pawn_JobTracker __instance, Verse.AI.JobCondition condition)
        {
            if (condition != Verse.AI.JobCondition.Incompletable) return;
            Verse.AI.Job job = __instance.curJob;
            if (job == null || job.def != RimWorld.JobDefOf.Hunt) return;
            Pawn hunter = trackerPawn(__instance);
            Thing victim = job.targetA.Thing;
            if (hunter == null || victim == null || !victim.Spawned) return;
            if (refused.Count > 256) refused.Clear(); // only ever holds recent failures, and a stale entry clears on first use
            refused[Key(hunter.thingIDNumber, victim.thingIDNumber)] = new Pair<IntVec3, int>(victim.Position, Find.TickManager.TicksGame);
        }

        static void HasPostfix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!__result) return;
            long key = Key(pawn.thingIDNumber, t.thingIDNumber);
            if (!refused.TryGetValue(key, out Pair<IntVec3, int> failed)) return;
            if (StillRefused(t.Position, failed, Find.TickManager.TicksGame)) { __result = false; return; }
            refused.Remove(key);
        }
    }

    static class CastSourceChain
    {
        // VFE Deserters and Vanilla Gravship Expanded each replace Thing.DrawPos in TryCastShot and DrawAimPie with their own
        // "barrel position" helper. The first replacement leaves nothing for the second to find, so a gravship turret's shots
        // and aim pie started at the turret centre. A last pass points whichever helper is there at one that knows both.
        static MethodInfo barrelsHelper, gravshipHelper;
        static Type barrelsType;
        static Func<Thing, Vector3> barrelsSource, gravshipSource;
        static int replaced;

        internal static void Apply() => Lp.Run("cast-source-chain", () =>
        {
            barrelsType = AccessTools.TypeByName("VFED.Building_TurretGunBarrels");
            barrelsHelper = barrelsType == null ? null : AccessTools.Method(barrelsType, "GetCastSource");
            if (barrelsHelper == null) return "VFE Deserters not loaded";
            gravshipHelper = AccessTools.Method("VanillaGravshipExpanded.Building_GravshipTurret:GetCastSource");
            if (gravshipHelper == null) return "Vanilla Gravship Expanded not loaded";
            barrelsSource = AccessTools.MethodDelegate<Func<Thing, Vector3>>(barrelsHelper);
            gravshipSource = AccessTools.MethodDelegate<Func<Thing, Vector3>>(gravshipHelper);
            Harmony harmony = new Harmony(Lp.Id);
            HarmonyMethod last = new HarmonyMethod(typeof(CastSourceChain), nameof(Transpiler)) { priority = Priority.Last };
            harmony.Patch(AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot"), transpiler: last);
            int inShot = replaced;
            harmony.Patch(AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawAimPie)), transpiler: last);
            int inPie = replaced - inShot;
            if (replaced == 0) return "neither helper is called in TryCastShot or DrawAimPie";
            Lp.Note = "TryCastShot " + inShot + ", DrawAimPie " + inPie;
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo either = AccessTools.Method(typeof(CastSourceChain), nameof(Either));
            foreach (CodeInstruction ins in code)
            {
                if (ins.operand is MethodInfo called && (called == barrelsHelper || called == gravshipHelper)) { ins.opcode = OpCodes.Call; ins.operand = either; replaced++; }
                yield return ins;
            }
        }

        // The gravship helper returns DrawPos for anything that is not a gravship turret.
        internal static Vector3 Either(Thing thing) => barrelsType.IsInstanceOfType(thing) ? barrelsSource(thing) : gravshipSource(thing);
    }

    static class GrassMapComponentCache
    {
        // Tall Grass: Hidden Danger reaches its own map components through Map.GetComponent on every call, including the
        // postfix that runs for every Plant.Graphic read: 302,986 of those in one 3600-tick measurement, 267 ms of it.
        // The vanilla getter walks the map's component list with a type test per entry, and a long mod list makes that
        // list long (171 component types in the save this was measured on), so the lookup costs more than the work it
        // leads to. Each call is pointed at a one-entry memo per component type, which hands back the same instance.
        static int replaced;

        internal static void Apply() => Lp.Run("grass-mapcomponent-cache", () =>
        {
            if (AccessTools.TypeByName("TallGrass.Concealment") == null) return "Tall Grass not loaded";
            Harmony harmony = new Harmony(Lp.Id);
            HarmonyMethod transpiler = new HarmonyMethod(typeof(GrassMapComponentCache), nameof(Transpiler));
            List<string> counts = new List<string>();
            // The methods the lookup was measured in, heaviest first. The others call it a handful of times a day.
            foreach (string name in new[] { "TallGrass.GrassGraphicPatch:Postfix", "TallGrass.Concealment:IsConcealed", "TallGrass.GrassGrowthPatch:Postfix", "TallGrass.GrassEcology:ProcessCell", "TallGrass.FootTrafficPatch:Postfix" })
            {
                string label = name.Substring(name.IndexOf('.') + 1);
                MethodInfo target = AccessTools.Method(name);
                if (target == null) { counts.Add(label + " missing"); continue; }
                int before = replaced;
                harmony.Patch(target, transpiler: transpiler);
                counts.Add(label + " " + (replaced - before));
            }
            if (replaced == 0) return "none of those methods calls Map.GetComponent any more";
            Lp.Note = string.Join(", ", counts);
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (CodeInstruction ins in code)
            {
                if (ins.operand is MethodInfo called && called.IsGenericMethod && called.DeclaringType == typeof(Map) && called.Name == nameof(Map.GetComponent))
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = AccessTools.Method(typeof(Cache<>).MakeGenericType(called.GetGenericArguments()[0]), nameof(Cache<MapComponent>.Of));
                    replaced++;
                }
                yield return ins;
            }
        }

        internal static class Cache<T> where T : MapComponent
        {
            static T held;

            // The field is read once, so an instance whose own map matches is the right one even while another thread
            // replaces it. A null is never kept: the component can be missing while the map is still being set up.
            internal static T Of(Map map)
            {
                T c = held;
                if (c != null && c.map == map) return c;
                c = map.GetComponent<T>(); // throws on a null map exactly as the call it replaces did
                if (c != null) held = c;
                return c;
            }
        }
    }

    static class GrassConcealmentInterval
    {
        // Tall Grass's map component walks every spawned pawn on every tick to deselect concealed hostiles, drop them as
        // enemy targets and interrupt attacks aimed at them: 63 us a tick with 102 pawns, with its component lookups
        // already cached. The pass runs once every 15 ticks instead, so a reaction can be up to 14 ticks late, a quarter
        // second at normal speed. The interval must divide 120: the same method's second half prunes stale grass
        // disturbances behind a TicksGame % 120 check, and an interval that missed those ticks would stop it running.
        // It cannot be offset per map for the same reason, so every map still makes its pass on the same tick.
        const int Interval = 15;

        internal static void Apply() => Lp.Run("grass-concealment-interval", () =>
        {
            MethodInfo tick = AccessTools.Method("TallGrass.GrassMapComponent:MapComponentTick");
            if (tick == null) return "Tall Grass not loaded";
            new Harmony(Lp.Id).Patch(tick, prefix: new HarmonyMethod(typeof(GrassConcealmentInterval), nameof(Prefix)));
            return null;
        });

        static bool Prefix() => Find.TickManager.TicksGame % Interval == 0;
    }

    static class Lp
    {
        internal const string Id = "local.lp17.hotfix";

        // A fix may set this to add detail to its "applied" line.
        internal static string Note;

        // apply returns null when done or the reason it was skipped; a throw counts as skipped.
        internal static void Run(string fix, Func<string> apply)
        {
            string skip;
            Note = null;
            try { skip = apply(); }
            catch (Exception e)
            {
                Exception b = e.GetBaseException();
                skip = b.GetType().Name + ": " + b.Message.Split('\n')[0].Trim();
            }
            Log.Message("[LP17 Hotfix] " + fix + ": " + (skip == null ? "applied" + (Note == null ? "" : " (" + Note + ")") : "skipped (" + skip + ")"));
        }
    }

    static class SoundBeforeUi
    {
        internal static void Apply() => Lp.Run("sound-before-ui", () =>
        {
            MethodInfo play = AccessTools.Method(typeof(SoundStarter), "PlayOneShot", new[] { typeof(SoundDef), typeof(SoundInfo) });
            if (play == null) return "SoundStarter.PlayOneShot not found";
            new Harmony(Lp.Id).Patch(play, new HarmonyMethod(typeof(SoundBeforeUi), nameof(Prefix)));
            return null;
        });

        // Root.soundRoot only exists after the InitializingInterface long event.
        [HarmonyPriority(Priority.First)]
        static bool Prefix() => Current.Root != null && Current.Root.soundRoot != null;
    }

    static class TrainsSpawnHook
    {
        static MethodInfo trainsPostfix;

        // VF's empty SpawnVehicles stub can be inlined, which would silently skip Trains' postfix on it;
        // run that postfix from a postfix on SpawnCaravanPawns, the only caller of the stub, instead.
        internal static void Apply() => Lp.Run("trains-spawn-hook", () =>
        {
            MethodInfo stub = AccessTools.Method("Vehicles.World.EnterMapUtilityVehicles:SpawnVehicles");
            MethodInfo spawn = AccessTools.Method("Vehicles.World.EnterMapUtilityVehicles:SpawnCaravanPawns");
            MethodInfo trains = AccessTools.Method("TrainsOfTheRim.Patches.Patch_EnterMap:RecallCaravanToTrainPosition");
            if (stub == null || spawn == null) return "Vehicle Framework spawn methods not found";
            if (trains == null || trains.GetParameters().Length != 6) return "Trains of the Rim postfix not found";

            bool onStub = false;
            Patches info = Harmony.GetPatchInfo(stub);
            if (info != null)
                foreach (Patch p in info.Postfixes)
                    if (p.PatchMethod == trains) onStub = true;
            if (!onStub) return "Trains of the Rim postfix is not on SpawnVehicles";

            trainsPostfix = trains;
            Harmony harmony = new Harmony(Lp.Id);
            harmony.Patch(spawn, new HarmonyMethod(typeof(TrainsSpawnHook), nameof(Prefix)), new HarmonyMethod(typeof(TrainsSpawnHook), nameof(Postfix)));
            try { harmony.Unpatch(stub, trains); }
            catch { harmony.Unpatch(spawn, HarmonyPatchType.All, Lp.Id); throw; } // never leave both hooks live
            return null;
        });

        // The caravan is emptied while pawns spawn, so keep a copy of the pawn list VF passes to the stub.
        // Last priority: the snapshot sees what the original sees.
        [HarmonyPriority(Priority.Last)]
        static void Prefix(Caravan caravan, out List<Pawn> __state) => __state = new List<Pawn>(caravan.PawnsListForReading);

        // First priority: Trains' hook used to run inside the original, ahead of every other postfix.
        [HarmonyPriority(Priority.First)]
        static void Postfix(Caravan caravan, Map map, IntVec3 enterCell, Rot4 edge, bool draftColonists, List<Pawn> __state, bool __runOriginal)
        {
            if (!__runOriginal) return; // the stub is only reached when the original runs
            trainsPostfix.Invoke(null, BindingFlags.DoNotWrapExceptions, null, new object[] { caravan, __state, map, enterCell, edge, draftColonists }, null);
        }
    }

    static class VgeSrrUnpatch
    {
        const string VgeId = "VanillaGravshipExpanded.SemiRandomResearchCompat";

        // Same call Semi Random Research queues as a long event, which runs after HugsLib's obsolete-patch check.
        // Static constructors of different mods run in no fixed order, so run VGE's now to be sure its patches exist.
        internal static void Apply() => Lp.Run("vge-srr-unpatch", () =>
        {
            if (AccessTools.TypeByName("CM_Semi_Random_Research.Compatibility") == null) return "Semi Random Research not loaded";
            Type vge = AccessTools.TypeByName("VanillaGravshipExpanded.SemiRandomResearch_Compat_Patches");
            if (vge != null) RuntimeHelpers.RunClassConstructor(vge.TypeHandle);
            if (!Harmony.HasAnyPatches(VgeId)) return "no Vanilla Gravship Expanded patches found";
            new Harmony(Lp.Id).UnpatchAll(VgeId);
            return null;
        });
    }

    static class SmyhKnownPatch
    {
        const string OwnerId = "wowgag.guiperformancepatch";

        // That owner's transpiler on DrawEquipmentAiming only swaps a cached comp lookup, but SMYH warns about unknown owners.
        internal static void Apply() => Lp.Run("smyh-known-patch", () =>
        {
            List<string> known = AccessTools.Field("ShowMeYourHands.ShowMeYourHandsMain:knownPatches")?.GetValue(null) as List<string>;
            if (known == null) return "ShowMeYourHandsMain.knownPatches not found";
            if (known.Contains(OwnerId)) return "already listed";
            known.Add(OwnerId);
            return null;
        });
    }

    static class DuplicateEquippable
    {
        // A CompEquippableAbility* replaces CompEquippable (vanilla uses comps Inherit="False" for that). Defs that
        // inherit BaseWeapon's CompEquippable as well get two comps with one verb owner ID, and every saved verb is
        // registered twice on load (Zeus Weapons Pack: CAT_ZeusStandard). Keep only the subclass.
        internal static void Apply() => Lp.Run("duplicate-equippable", () =>
        {
            int defs = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.comps == null) continue;
                bool sub = false;
                foreach (CompProperties p in def.comps)
                    if (p.compClass != typeof(CompEquippable) && typeof(CompEquippable).IsAssignableFrom(p.compClass)) sub = true;
                if (sub && def.comps.RemoveAll(p => p.compClass == typeof(CompEquippable)) > 0) defs++;
            }
            Lp.Note = defs + " defs";
            return null;
        });
    }

    static class PatchTargets
    {
        // {mod, defName in the xpath, new defName or null to drop the operation, condition}
        static readonly (string mod, string def, string to, Func<bool> when)[] Rules =
        {
            // VFE Security 1.7 loads its 1.6 defs, which no longer have these 1.5 turrets.
            ("issaczhuang.muzzleflash", "VFES_Gun_HMGComplex", null, () => true),
            ("issaczhuang.muzzleflash", "VFES_Gun_SentryTurret", null, () => true),
            ("issaczhuang.muzzleflash", "VFES_Artillery_Weapon", null, () => true),
            ("issaczhuang.muzzleflash", "VFES_Gun_ChargeComplex", null, () => true),
            // Rimsenal Augmented Vanilla dropped its smart and nest guns after its 1.4 folder.
            ("issaczhuang.muzzleflash", "Gun_SmartRifle", null, () => true),
            ("issaczhuang.muzzleflash", "Gun_SmartPistol", null, () => true),
            ("issaczhuang.muzzleflash", "Gun_SMG", null, () => true),
            ("issaczhuang.muzzleflash", "SmartMG", null, () => true),
            ("issaczhuang.muzzleflash", "Gun_SmartSniperRifle", null, () => true),
            ("issaczhuang.muzzleflash", "Gun_NestMMG", null, () => true),
            ("issaczhuang.muzzleflash", "Gun_NestMissile", null, () => true),
            // Vanilla Weapons Expanded moved its musket and flintlock to Medieval 2.
            ("issaczhuang.muzzleflash", "VWE_Gun_Musket", "VFEM2_Gun_Musket", () => Active("OskarPotocki.VFE.Medieval2")),
            ("issaczhuang.muzzleflash", "VWE_Gun_Flintlock", "VFEM2_Gun_Flintlock", () => Active("OskarPotocki.VFE.Medieval2")),
            // Only the Combat Extended folder of All Bamba Turrets defines the charge AA gun.
            ("bambaryla.allbambaturrets", "ABT_Gun_ChargeAA", null, () => !Active("CETeam.CombatExtended")),
        };

        static readonly FieldInfo XPath = AccessTools.Field(typeof(PatchOperationPathed), "xpath");
        static int dropped, retargeted;

        // Muzzle Flash's SafePatcher warns while patching when an xpath finds no def, so these operations are
        // rewritten in the mods' loaded patch lists before the patches run.
        internal static void Apply() => Lp.Run("patch-targets", () =>
        {
            if (XPath == null) return "PatchOperationPathed.xpath not found";
            dropped = retargeted = 0;
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                var rules = Array.FindAll(Rules, r => string.Equals(mod.PackageIdPlayerFacing, r.mod, StringComparison.OrdinalIgnoreCase));
                if (rules.Length > 0) Walk((List<PatchOperation>)mod.Patches, rules);
            }
            if (dropped + retargeted == 0) return "no matching patch operation";
            Lp.Note = dropped + " dropped, " + retargeted + " retargeted";
            return null;
        });

        // ModsConfig.IsActive compares the raw list entries, which carry a "_steam" suffix for workshop copies.
        static bool Active(string id)
        {
            foreach (ModContentPack m in LoadedModManager.RunningMods)
                if (string.Equals(m.PackageIdPlayerFacing, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void Walk(List<PatchOperation> ops, (string mod, string def, string to, Func<bool> when)[] rules)
        {
            for (int i = ops.Count - 1; i >= 0; i--)
                if (ops[i] != null && !Fix(ops[i], rules)) { ops.RemoveAt(i); dropped++; }
        }

        // Returns false when the operation must be removed; recurses into sequences, FindMod and conditionals.
        static bool Fix(PatchOperation op, (string mod, string def, string to, Func<bool> when)[] rules)
        {
            if (op is PatchOperationPathed && XPath.GetValue(op) is string xpath)
                foreach (var r in rules)
                {
                    string target = "defName=\"" + r.def + "\"";
                    if (!xpath.Contains(target) || !r.when()) continue;
                    if (r.to == null) return false;
                    XPath.SetValue(op, xpath.Replace(target, "defName=\"" + r.to + "\""));
                    retargeted++;
                }
            for (Type t = op.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (typeof(List<PatchOperation>).IsAssignableFrom(f.FieldType))
                    {
                        if (f.GetValue(op) is List<PatchOperation> list) Walk(list, rules);
                    }
                    else if (typeof(PatchOperation).IsAssignableFrom(f.FieldType) && f.GetValue(op) is PatchOperation child && !Fix(child, rules))
                    {
                        f.SetValue(op, null);
                        dropped++;
                    }
                }
            return true;
        }
    }

    static class BackCompatOfPlayer
    {
        static readonly MethodInfo OfPlayer = AccessTools.PropertyGetter(typeof(RimWorld.Faction), nameof(RimWorld.Faction.OfPlayer));

        // Vanilla tests pawn.Faction == Faction.OfPlayer before Scribe.mode == PostLoadInit, so a mechanoid saved
        // inside a quest (a pending drop pod) throws while the quest list loads, before the world exists.
        internal static void Apply() => Lp.Run("backcompat-ofplayer", () =>
        {
            MethodInfo target = AccessTools.Method(typeof(BackCompatibilityConverter_Universal), "PostExposeData");
            if (target == null || OfPlayer == null) return "BackCompatibilityConverter_Universal.PostExposeData not found";
            new Harmony(Lp.Id).Patch(target, transpiler: new HarmonyMethod(typeof(BackCompatOfPlayer), nameof(Transpiler)));
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            NoticeSwap.Replace(code, null, OfPlayer, AccessTools.Method(typeof(BackCompatOfPlayer), nameof(OfPlayerOrNull)));

        // Without a world the mode is not PostLoadInit yet, so the vanilla condition is false either way.
        static RimWorld.Faction OfPlayerOrNull() => Current.Game?.World?.factionManager == null ? null : RimWorld.Faction.OfPlayer;
    }

    static class VgeSpawnResume
    {
        static Type part;
        static FieldInfo spawned, inSignal;

        // VGE's QuestPart_SpawnThingInteractive opens a map targeter on the quest's accept signal, but a targeter is
        // not saved: a reload before the click leaves the quest ongoing forever with its ship never spawned.
        internal static void Apply() => Lp.Run("vge-spawn-resume", () =>
        {
            part = AccessTools.TypeByName("VanillaGravshipExpanded.QuestPart_SpawnThingInteractive");
            if (part == null) return "Vanilla Gravship Expanded not loaded";
            spawned = AccessTools.Field(part, "spawned");
            inSignal = AccessTools.Field(part, "inSignal");
            if (spawned == null || inSignal == null) return "QuestPart_SpawnThingInteractive fields not found";
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(Game), nameof(Game.LoadGame)), postfix: new HarmonyMethod(typeof(VgeSpawnResume), nameof(Postfix)));
            return null;
        });

        static void Postfix()
        {
            foreach (RimWorld.Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.State != RimWorld.QuestState.Ongoing) continue;
                foreach (RimWorld.QuestPart p in q.PartsListForReading)
                {
                    // Only the accept signal is known to have fired already for an ongoing quest.
                    string signal = part.IsInstanceOfType(p) ? (string)inSignal.GetValue(p) : null;
                    if (signal != "Quest" + q.id + ".Initiate" || (bool)spawned.GetValue(p)) continue;
                    RimWorld.QuestPart resume = p;
                    LongEventHandler.ExecuteWhenFinished(() => resume.Notify_QuestSignalReceived(new RimWorld.Signal(signal)));
                    Log.Message("[LP17 Hotfix] vge-spawn-resume: reopened the landing targeter of quest '" + q.name + "'");
                }
            }
        }
    }

    static class CinecamSceneReset
    {
        static MethodInfo allWorkers;

        // Cinematic Camera keeps its scene workers in a static registry, so their target buffers (letter records,
        // shot queues, framed pawns) outlive a game load and point at the old game's pawns and factions.
        internal static void Apply() => Lp.Run("cinecam-scene-reset", () =>
        {
            allWorkers = AccessTools.Method("CinematicCamera.SceneRegistry:AllWorkers");
            if (allWorkers == null) return "Cinematic Camera not loaded";
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit)), postfix: new HarmonyMethod(typeof(CinecamSceneReset), nameof(Postfix)));
            return null;
        });

        static void Postfix()
        {
            int cleared = 0;
            foreach (object w in (IEnumerable<object>)allWorkers.Invoke(null, null))
            {
                AccessTools.Method(w.GetType(), "Cleanup")?.Invoke(w, null);
                for (Type t = w.GetType(); t != null && t != typeof(object); t = t.BaseType)
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        object c = f.GetValue(w);
                        MethodInfo clear = c == null || c.GetType().Namespace != "System.Collections.Generic" ? null : c.GetType().GetMethod("Clear", Type.EmptyTypes);
                        if (clear == null) continue;
                        clear.Invoke(c, null);
                        cleared++;
                    }
            }
            if (cleared > 0) Log.Message("[LP17 Hotfix] cinecam-scene-reset: cleared " + cleared + " scene buffers");
        }
    }

    static class CryoTankUnspawned
    {
        // [sbz] Cryogenic Tank reads target.Map without checking it, so right-clicking a pawn that is being
        // carried (not spawned, Map null) throws inside the float menu.
        internal static void Apply() => Lp.Run("cryotank-unspawned", () =>
        {
            MethodInfo make = AccessTools.Method("sbz.Display.FloatMenuOptionProvider_CarryToCryogenicTank:MakeOption");
            if (make == null) return "Cryogenic Tank float menu provider not found";
            new Harmony(Lp.Id).Patch(make, new HarmonyMethod(typeof(CryoTankUnspawned), nameof(Prefix)));
            return null;
        });

        static bool Prefix(Thing target, ref FloatMenuOption __result)
        {
            if (target != null && target.Spawned) return true;
            __result = null;
            return false;
        }
    }

    static class GoreFootprintType
    {
        // Gore Perfected looks for Verse.PawnFootprintMaker, but the class is RimWorld.PawnFootprintMaker, so its hook
        // that hides vanilla snow prints is never placed. Place it with the right type (its warning is already logged).
        internal static void Apply() => Lp.Run("gore-footprint-type", () =>
        {
            Type patch = AccessTools.TypeByName("GorePerfected.FootprintPatch");
            if (patch == null) return "Gore Perfected not loaded";
            FieldInfo maker = AccessTools.Field(patch, "makerPawn");
            MethodInfo prefix = AccessTools.Method(patch, "VanillaFootprint_Prefix");
            MethodInfo place = AccessTools.Method(typeof(RimWorld.PawnFootprintMaker), "TryPlaceFootprint");
            FieldInfo pawn = AccessTools.Field(typeof(RimWorld.PawnFootprintMaker), "pawn");
            if (maker == null || prefix == null || place == null || pawn == null) return "footprint members not found";
            if (maker.GetValue(null) != null) return "Gore Perfected found the footprint code itself";
            maker.SetValue(null, pawn);
            new Harmony(Lp.Id).Patch(place, new HarmonyMethod(prefix));
            return null;
        });
    }

    static class GppMarketValueCache
    {
        // WOWGAG's Performance Patch caches def stats as constants, but MarketValue follows ingredient prices that
        // other mods adjust during play; its self-check then finds a mismatch, warns and drops the whole cache.
        internal static void Apply() => Lp.Run("gpp-marketvalue-cache", () =>
        {
            MethodInfo keyOf = AccessTools.Method("GUIPerformancePatch.GppDefStat:KeyOf");
            if (keyOf == null) return "GppDefStat.KeyOf not found";
            new Harmony(Lp.Id).Patch(keyOf, new HarmonyMethod(typeof(GppMarketValueCache), nameof(Prefix)));
            return null;
        });

        static bool Prefix(RimWorld.StatDef stat, ref long key, ref bool __result)
        {
            if (stat != RimWorld.StatDefOf.MarketValue) return true;
            key = 0;
            __result = false;
            return false;
        }
    }

    static class MapRemovedRefs
    {
        static Type cinecam;
        static FieldInfo scanTick;

        // A removed map stays referenced by joiner letters (saved, so every later load warns it cannot resolve the map)
        // and by Cinematic Camera's map list, which it rescans on a timer and so may switch to the removed map.
        internal static void Apply() => Lp.Run("map-removed-refs", () =>
        {
            cinecam = AccessTools.TypeByName("CinematicCamera.CinematicCameraManager");
            scanTick = cinecam == null ? null : AccessTools.Field(cinecam, "directorScanTick");
            new Harmony(Lp.Id).Patch(AccessTools.Method(typeof(Game), nameof(Game.DeinitAndRemoveMap)),
                new HarmonyMethod(typeof(MapRemovedRefs), nameof(Prefix)), new HarmonyMethod(typeof(MapRemovedRefs), nameof(Postfix)));
            Lp.Note = scanTick == null ? "letters only" : "letters, Cinematic Camera";
            return null;
        });

        static void Prefix(Map map)
        {
            foreach (Letter l in Find.LetterStack.LettersListForReading)
                if (l is RimWorld.ChoiceLetter_AcceptJoiner j && j.overrideMap == map) j.overrideMap = null;
            foreach (RimWorld.IArchivable a in Find.Archive.ArchivablesListForReading)
                if (a is RimWorld.ChoiceLetter_AcceptJoiner j && j.overrideMap == map) j.overrideMap = null;
        }

        static void Postfix()
        {
            if (scanTick == null) return;
            GameComponent c = Current.Game.components.Find(x => x.GetType() == cinecam);
            if (c != null) scanTick.SetValue(c, -1); // rescan before the next pick
        }
    }

    static class VfeArtCreatorPrune
    {
        static FieldInfo instance, artCreator;

        // VFE Empire records who made each artwork but never drops an entry, so art that was destroyed or left on an
        // abandoned map is saved as a null key and logs "Null key while loading dictionary" on every load.
        internal static void Apply() => Lp.Run("vfe-artcreator-prune", () =>
        {
            Type comp = AccessTools.TypeByName("VFEEmpire.GameComponent_Empire");
            if (comp == null) return "VFE Empire not loaded";
            instance = AccessTools.Field(comp, "Instance");
            artCreator = AccessTools.Field(comp, "artCreator");
            if (instance == null || artCreator == null) return "GameComponent_Empire fields not found";
            Harmony h = new Harmony(Lp.Id);
            h.Patch(AccessTools.Method(comp, "ExposeData"), new HarmonyMethod(typeof(VfeArtCreatorPrune), nameof(SavePrefix)));
            h.Patch(AccessTools.Method(typeof(Game), nameof(Game.DeinitAndRemoveMap)), new HarmonyMethod(typeof(VfeArtCreatorPrune), nameof(MapPrefix)));
            return null;
        });

        static Dictionary<ThingWithComps, Pawn> Art(object comp) => comp == null ? null : (Dictionary<ThingWithComps, Pawn>)artCreator.GetValue(comp);

        static void SavePrefix(object __instance)
        {
            if (Scribe.mode == LoadSaveMode.Saving) Art(__instance)?.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed);
        }

        // MapHeld is only right before the map leaves Find.Maps.
        static void MapPrefix(Map map) => Art(instance.GetValue(null))?.RemoveAll(kv => kv.Key != null && kv.Key.MapHeld == map);
    }

    static class VaeLaunchFaction
    {
        static readonly MethodInfo isPlayer = AccessTools.PropertyGetter(typeof(RimWorld.Faction), nameof(RimWorld.Faction.IsPlayer));

        // Vanilla Achievements Expanded counts crew and animals at gravship takeoff with x.Faction.IsPlayer, which throws
        // for wild animals (no faction); each throw makes VAE unlock the card unearned and log an error.
        internal static void Apply() => Lp.Run("vae-launch-faction", () =>
        {
            Type tracker = AccessTools.TypeByName("AchievementsExpanded.GravshipLaunchContentsTracker");
            if (tracker == null) return "Vanilla Achievements Expanded not loaded";
            Harmony h = new Harmony(Lp.Id);
            int patched = 0;
            foreach (Type n in tracker.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodInfo m in n.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!Calls(m)) continue;
                    h.Patch(m, transpiler: new HarmonyMethod(typeof(VaeLaunchFaction), nameof(Transpiler)));
                    patched++;
                }
            if (patched == 0) return "no Faction.IsPlayer call in the tracker's lambdas";
            Lp.Note = patched + " lambdas";
            return null;
        });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (CodeInstruction i in code)
            {
                if (i.Calls(isPlayer)) { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(VaeLaunchFaction), nameof(IsPlayer)); }
                yield return i;
            }
        }

        static bool Calls(MethodInfo m)
        {
            foreach (KeyValuePair<OpCode, object> i in PatchProcessor.ReadMethodBody(m))
                if (Equals(i.Value, isPlayer)) return true;
            return false;
        }

        static bool IsPlayer(RimWorld.Faction f) => f != null && f.IsPlayer;
    }

    static class WqRitualNoOutcomes
    {
        // Wants and Quirks asks every quality ritual for a random outcome, but rituals like the gravship launch have no
        // outcome chances, so the empty weighted pick logs an error. The mod already reads a null outcome as not positive.
        internal static void Apply() => Lp.Run("wq-ritual-no-outcomes", () =>
        {
            MethodInfo m = AccessTools.Method("WantsAndQuirks.LordJob_Ritual_ApplyOutcome_Patch:RitualOutcomePositive");
            if (m == null) return "Wants and Quirks not loaded";
            new Harmony(Lp.Id).Patch(m, new HarmonyMethod(typeof(WqRitualNoOutcomes), nameof(Prefix)));
            return null;
        });

        static bool Prefix(RimWorld.LordJob_Ritual ritual, ref bool __result)
        {
            if (!(ritual?.Ritual?.outcomeEffect is RimWorld.RitualOutcomeEffectWorker_FromQuality w) || !w.def.outcomeChances.NullOrEmpty()) return true;
            __result = false;
            return false;
        }
    }

    static class GiddyUpMapClose
    {
        static Map closing;

        // When a map closes, despawning a mount ends Giddy-Up's mounted job, whose finish action sends a non-player mount
        // near the edge off the map: it is despawned and passed to the world inside the despawn, which then errors twice.
        internal static void Apply() => Lp.Run("giddyup-map-close", () =>
        {
            if (AccessTools.TypeByName("GiddyUp.Jobs.JobDriver_Mounted") == null) return "Giddy-Up not loaded";
            Harmony h = new Harmony(Lp.Id);
            h.Patch(AccessTools.Method("Verse.MapDeiniter:PassPawnsToWorld"), new HarmonyMethod(typeof(GiddyUpMapClose), nameof(PassPrefix)),
                finalizer: new HarmonyMethod(typeof(GiddyUpMapClose), nameof(PassFinalizer)));
            h.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.ExitMap)), new HarmonyMethod(typeof(GiddyUpMapClose), nameof(ExitPrefix)));
            return null;
        });

        static void PassPrefix(Map map) => closing = map;
        static void PassFinalizer() => closing = null;

        // The closing map passes every pawn to the world itself.
        static bool ExitPrefix(Pawn __instance) => closing == null || __instance.MapHeld != closing;
    }

    static class FoodVarietyReset
    {
        static FieldInfo diets, needers;

        // VE Food Variety keeps its pawn diets in static fields that a new game or load never clears, so pawns of the
        // previous game are saved into the next one and fail to resolve on every later load.
        internal static void Apply() => Lp.Run("foodvariety-reset", () =>
        {
            Type comp = AccessTools.TypeByName("VanillaCookingExpandedVariety.GameComponent_FoodVariety");
            if (comp == null) return "VE Food Variety not loaded";
            diets = AccessTools.Field(comp, "pawns_and_diet");
            needers = AccessTools.Field(comp, "colonists_with_foodvariety_need");
            if (diets == null || needers == null) return "GameComponent_FoodVariety fields not found";
            new Harmony(Lp.Id).Patch(AccessTools.Constructor(comp, new[] { typeof(Game) }), postfix: new HarmonyMethod(typeof(FoodVarietyReset), nameof(Postfix)));
            return null;
        });

        static void Postfix()
        {
            (diets.GetValue(null) as System.Collections.IDictionary)?.Clear();
            (needers.GetValue(null) as System.Collections.IList)?.Clear();
        }
    }

    static class GzpSowWithoutBill
    {
        static MethodInfo setRequire;

        // Growing Zones Plus refuses to sow a zone without an active bill, but a zone whose plant was set directly
        // (not through its bills tab) has none, so growers never touch it. A zone with no bills sows its chosen plant.
        internal static void Apply() => Lp.Run("gzp-sow-without-bill", () =>
        {
            Type zone = AccessTools.TypeByName("Growing_Zones_Plus.Zone_GrowingPlus");
            if (zone == null) return "Growing Zones Plus not loaded";
            setRequire = AccessTools.PropertySetter(zone, "RequireActiveBillToSow");
            if (setRequire == null) return "RequireActiveBillToSow not found";
            Harmony h = new Harmony(Lp.Id);
            foreach (ConstructorInfo c in AccessTools.GetDeclaredConstructors(zone))
                h.Patch(c, postfix: new HarmonyMethod(typeof(GzpSowWithoutBill), nameof(CtorPostfix)));
            h.Patch(AccessTools.Method(zone, "ExposeData"), postfix: new HarmonyMethod(typeof(GzpSowWithoutBill), nameof(ExposePostfix)));
            return null;
        });

        static void CtorPostfix(object __instance) => setRequire.Invoke(__instance, new object[] { false });

        static void ExposePostfix(object __instance)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit && ((RimWorld.IBillGiver)__instance).BillStack.Count == 0) setRequire.Invoke(__instance, new object[] { false });
        }
    }

    static class YartLayerSeparation
    {
        static Action<object> run;

        internal static void Apply() => Lp.Run("yart-layer-separation", () =>
        {
            MethodInfo validate = AccessTools.Method("YART.SugiyamaLayout:ValidateLayerSeparation");
            MethodInfo separation = AccessTools.Method("YART.SugiyamaLayout:SeparationOf");
            Type node = AccessTools.TypeByName("YART.Data.ResearchNode");
            if (validate == null || separation == null || node == null) return "YART layout methods not found";
            MethodInfo getPos = AccessTools.PropertyGetter(node, "Position");
            MethodInfo setPos = AccessTools.PropertySetter(node, "Position");
            if (getPos == null || setPos == null) return "ResearchNode.Position not found";

            object fixer = Activator.CreateInstance(typeof(LayerFixer<>).MakeGenericType(node), getPos, setPos, separation);
            run = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), fixer, "Run");
            new Harmony(Lp.Id).Patch(validate, new HarmonyMethod(typeof(YartLayerSeparation), nameof(Prefix)));
            return null;
        });

        static void Prefix(object layers) => run(layers);
    }

    // Typed over YART's node class at runtime so the delegates are built once and calls stay direct.
    sealed class LayerFixer<N> where N : class
    {
        readonly Func<N, Vector2> getPos;
        readonly Action<N, Vector2> setPos;
        readonly Func<N, N, float> sep;

        public LayerFixer(MethodInfo getPos, MethodInfo setPos, MethodInfo sep)
        {
            this.getPos = (Func<N, Vector2>)Delegate.CreateDelegate(typeof(Func<N, Vector2>), getPos);
            this.setPos = (Action<N, Vector2>)Delegate.CreateDelegate(typeof(Action<N, Vector2>), setPos);
            this.sep = (Func<N, N, float>)Delegate.CreateDelegate(typeof(Func<N, N, float>), sep);
        }

        // AssignY snaps y to the grid after relaxation, which can leave two nodes of a rank on one y.
        public void Run(object layers)
        {
            foreach (List<N> layer in (List<List<N>>)layers)
                for (int i = 1; i < layer.Count; i++)
                {
                    float min = getPos(layer[i - 1]).y + sep(layer[i - 1], layer[i]);
                    Vector2 p = getPos(layer[i]);
                    if (p.y < min - 0.5f) setPos(layer[i], new Vector2(p.x, min));
                }
        }
    }

    // Shared by the notice fixes: reroutes the logging call of chosen notices (to info level or to a filter).
    static class NoticeSwap
    {
        // Retargets calls to `from` to `to` where the string literals built for the call contain `marker`
        // (null marker: every call). Throws, so the method stays unpatched, if no call was retargeted.
        internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> code, string marker, MethodInfo from, MethodInfo to)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            int replaced = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(from)) continue;
                bool hit = marker == null;
                for (int j = i - 1; j >= 0 && !hit; j--)
                {
                    if (list[j].Calls(from) || list[j].Calls(to)) break; // the previous log statement
                    hit = list[j].opcode == OpCodes.Ldstr && ((string)list[j].operand).Contains(marker);
                }
                if (!hit) continue;
                list[i].operand = to;
                replaced++;
            }
            if (replaced == 0) throw new InvalidOperationException("no call matched " + (marker == null ? "(any)" : marker.Trim()));
            return list;
        }
    }

    static class PwuInfoNotices
    {
        static readonly MethodInfo Warning = AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) });
        static readonly MethodInfo Message = AccessTools.Method(typeof(Log), "Message", new[] { typeof(string) });

        internal static void Apply() => Lp.Run("pwu-info-notices", () =>
        {
            MethodInfo init = AccessTools.Method("PersonaWeaponsUnbound.WeaponRegistry:Initialize");
            MethodInfo pairs = AccessTools.Method("PersonaWeaponsUnbound.WeaponRegistry:WarnOnPairConflicts");
            if (init == null || pairs == null) return "WeaponRegistry methods not found";
            Harmony harmony = new Harmony(Lp.Id);
            harmony.Patch(init, transpiler: new HarmonyMethod(typeof(PwuInfoNotices), nameof(NoBaseWeapon)));
            try { harmony.Patch(pairs, transpiler: new HarmonyMethod(typeof(PwuInfoNotices), nameof(PairConflicts))); }
            catch { harmony.Unpatch(init, HarmonyPatchType.Transpiler, Lp.Id); throw; } // both notices or none
            return null;
        });

        static IEnumerable<CodeInstruction> NoBaseWeapon(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, "No base weapon detected", Warning, Message);

        static IEnumerable<CodeInstruction> PairConflicts(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, "maps to multiple persona variants", Warning, Message);
    }

    static class MeleeAnimInfoNotices
    {
        // AM logs through Core.Warn / Core.Log (same prefix, warning vs message); only these call sites change.
        static MethodInfo warn, info;

        internal static void Apply() => Lp.Run("meleeanim-info-notices", () =>
        {
            MethodInfo conflicts = AccessTools.Method("AM.Core:LogPotentialConflicts");
            MethodInfo tweaks = AccessTools.Method("AM.Core:LoadAllTweakData");
            warn = AccessTools.Method("AM.Core:Warn");
            info = AccessTools.Method("AM.Core:Log");
            if (conflicts == null || tweaks == null || warn == null || info == null) return "AM.Core methods not found";
            Harmony harmony = new Harmony(Lp.Id);
            harmony.Patch(conflicts, transpiler: new HarmonyMethod(typeof(MeleeAnimInfoNotices), nameof(PatchConflicts)));
            try { harmony.Patch(tweaks, transpiler: new HarmonyMethod(typeof(MeleeAnimInfoNotices), nameof(MissingTweakData))); }
            catch { harmony.Unpatch(conflicts, HarmonyPatchType.Transpiler, Lp.Id); throw; } // both notices or none
            return null;
        });

        // The header and the per-patch list are the only warnings in this method.
        static IEnumerable<CodeInstruction> PatchConflicts(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, null, warn, info);

        static IEnumerable<CodeInstruction> MissingTweakData(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, "missing weapon tweak data", warn, info);
    }

    static class ScosNoticeVideoPlayerUtil
    {
        const string Marker = " probably needs a StaticConstructorOnStartup attribute";
        const string Notice = "Type VideoPlayerUtil" + Marker;

        static bool videoPlayerUtilExists; // read on the thread-pool threads that run the check
        static readonly MethodInfo Warning = AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) });
        static readonly MethodInfo Route = AccessTools.Method(typeof(ScosNoticeVideoPlayerUtil), nameof(Warn));

        // VideoPlayerUtil builds its video object on first use, from UI code, so the check's warning is a false
        // positive and the attribute would create that object at launch. Other types keep their warning.
        internal static void Apply() => Lp.Run("scos-notice-videoplayerutil", () =>
        {
            videoPlayerUtilExists = AccessTools.TypeByName("AM.Video.VideoPlayerUtil") != null;
            if (!videoPlayerUtilExists) return "AM.Video.VideoPlayerUtil not found";
            MethodInfo check = FindCheck();
            if (check == null) return "attribute check not found";
            new Harmony(Lp.Id).Patch(check, transpiler: new HarmonyMethod(typeof(ScosNoticeVideoPlayerUtil), nameof(Reroute)));
            return null;
        });

        // The lambda inside ReportProbablyMissingAttributes; its compiler-given name can change, its text does not.
        static MethodInfo FindCheck()
        {
            BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type nested in typeof(StaticConstructorOnStartupUtility).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodInfo m in nested.GetMethods(all))
                {
                    if (m.GetMethodBody() == null) continue;
                    foreach (KeyValuePair<OpCode, object> ins in PatchProcessor.ReadMethodBody(m))
                        if (ins.Key == OpCodes.Ldstr && ((string)ins.Value).Contains(Marker)) return m;
                }
            return null;
        }

        static IEnumerable<CodeInstruction> Reroute(IEnumerable<CodeInstruction> code) => NoticeSwap.Replace(code, Marker, Warning, Route);

        static void Warn(string text)
        {
            if (videoPlayerUtilExists && text != null && text.StartsWith(Notice, StringComparison.Ordinal)) Log.Message(text);
            else Log.Warning(text);
        }
    }

    static class TranslationDump
    {
        const string KeyedDuplicate = "Duplicate keyed translation key: ";
        static string summary;

        // Dev mode only. Runs after the startup pass has injected translations, so the lists are final.
        internal static void Apply()
        {
            summary = null;
            Lp.Run("translation-dump", () =>
            {
                if (!Prefs.DevMode) return "dev mode is off";
                LoadedLanguage lang = LanguageDatabase.activeLanguage;
                if (lang == null) return "no active language";

                Dictionary<string, List<string[]>> files = null; // DefInjected file name -> {path, mod folder}
                StringBuilder tsv = new StringBuilder("source\tkind\tdefType\tdefName\tmodFolder\tfile\ttext\n");
                int general = lang.loadErrors.Count, defInjected = 0, notInjected = 0;

                foreach (string e in lang.loadErrors)
                {
                    string file = XmlPathIn(e);
                    if (file.Length == 0 && e.StartsWith(KeyedDuplicate, StringComparison.Ordinal))
                    {
                        string key = e.Substring(KeyedDuplicate.Length);
                        int at = key.IndexOf(" in language ", StringComparison.Ordinal);
                        LoadedLanguage.KeyedReplacement kr;
                        if (at > 0 && lang.keyedReplacements.TryGetValue(key.Substring(0, at), out kr)) file = kr.fileSourceFullPath ?? "";
                    }
                    Row(tsv, "general", "error", "", "", ModOf(file), file, e);
                }

                foreach (DefInjectionPackage p in lang.defInjections)
                {
                    // The game counts p.loadErrors; the injections that failed to apply give each error its def and file.
                    List<DefInjectionPackage.DefInjection> failed = new List<DefInjectionPackage.DefInjection>();
                    foreach (DefInjectionPackage.DefInjection inj in p.injections.Values)
                        if (!inj.injected && !inj.isPlaceholder) failed.Add(inj);
                    HashSet<DefInjectionPackage.DefInjection> explained = new HashSet<DefInjectionPackage.DefInjection>();

                    foreach (string e in p.loadErrors)
                    {
                        DefInjectionPackage.DefInjection hit = null;
                        foreach (DefInjectionPackage.DefInjection inj in failed)
                            if (inj.path.Length > 0 && e.Contains(inj.path) && (hit == null || inj.path.Length > hit.path.Length)) hit = inj;
                        if (hit != null) explained.Add(hit);
                        if (files == null) files = DefInjectedFiles(lang);
                        DefRow(tsv, files, p, hit != null ? hit.DefName : "", hit != null ? hit.fileSource : DefFileIn(e), "error", e);
                        defInjected++;
                    }
                    foreach (DefInjectionPackage.DefInjection inj in failed)
                    {
                        if (explained.Contains(inj)) continue;
                        if (files == null) files = DefInjectedFiles(lang);
                        DefRow(tsv, files, p, inj.DefName, inj.fileSource, "notinjected", "not injected and no load error names it: " + inj.path);
                        notInjected++;
                    }
                }

                string path = Path.Combine(GenFilePaths.ConfigFolderPath, "TranslationErrors-" + lang.FriendlyNameEnglish + ".tsv");
                File.WriteAllText(path, tsv.ToString(), new UTF8Encoding(false));
                Lp.Note = (general + defInjected + notInjected) + " rows";
                summary = "game total " + (general + defInjected) + " (" + general + " general + " + defInjected + " def-injected), plus " + notInjected + " not-injected rows";
                return null;
            });
            if (summary != null) Log.Message("[LP17 Hotfix] translation-dump: " + summary);
        }

        static void DefRow(StringBuilder tsv, Dictionary<string, List<string[]>> files, DefInjectionPackage p, string defName, string fileName, string kind, string text)
        {
            List<string> paths = new List<string>(), mods = new List<string>();
            List<string[]> found;
            if (!string.IsNullOrEmpty(fileName) && files.TryGetValue(fileName, out found))
            {
                foreach (string[] f in found) if (InDefTypeDir(f[0], p.defType)) { paths.Add(f[0]); if (!mods.Contains(f[1])) mods.Add(f[1]); }
                if (paths.Count == 0) // unusual folder name: list every file of that name
                    foreach (string[] f in found) { paths.Add(f[0]); if (!mods.Contains(f[1])) mods.Add(f[1]); }
            }
            Row(tsv, "definjected", kind, p.defType.Name, defName, string.Join("|", mods), paths.Count > 0 ? string.Join(" | ", paths) : fileName ?? "", text);
        }

        static void Row(StringBuilder tsv, string source, string kind, string defType, string defName, string mod, string file, string text)
        {
            string[] cells = { source, kind, defType, defName, mod, file, text };
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) tsv.Append('\t');
                tsv.Append((cells[i] ?? "").Replace("\t", " ").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n"));
            }
            tsv.Append('\n');
        }

        // Every English DefInjected file of every running mod, by file name.
        static Dictionary<string, List<string[]>> DefInjectedFiles(LoadedLanguage lang)
        {
            Dictionary<string, List<string[]>> index = new Dictionary<string, List<string[]>>();
            string[] names = lang.LegacyFolderName == lang.folderName ? new[] { lang.folderName } : new[] { lang.folderName, lang.LegacyFolderName };
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
                foreach (string folder in mod.foldersToLoadDescendingOrder)
                    foreach (string name in names)
                    {
                        string dir = Path.Combine(folder, "Languages", name, "DefInjected");
                        if (!Directory.Exists(dir)) continue;
                        foreach (string f in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
                        {
                            string key = Path.GetFileName(f);
                            List<string[]> list;
                            if (!index.TryGetValue(key, out list)) index[key] = list = new List<string[]>();
                            if (!list.Exists(x => x[0] == f)) list.Add(new[] { f, mod.FolderName });
                        }
                    }
            return index;
        }

        // The game accepts the type name, the full name, or either plus one trailing character as folder name.
        static bool InDefTypeDir(string path, Type defType)
        {
            string[] parts = path.Split('\\', '/');
            int at = Array.LastIndexOf(parts, "DefInjected");
            if (at < 0 || at + 1 >= parts.Length) return false;
            string dir = parts[at + 1];
            string cut = dir.Length > 1 ? dir.Substring(0, dir.Length - 1) : dir;
            return dir == defType.Name || dir == defType.FullName || cut == defType.Name || cut == defType.FullName;
        }

        static string ModOf(string path)
        {
            if (path.Length > 0)
                foreach (ModContentPack mod in LoadedModManager.RunningMods)
                    if (path.StartsWith(mod.RootDir, StringComparison.OrdinalIgnoreCase)) return mod.FolderName;
            return "";
        }

        // First "X:\...\name.xml" in a message.
        static string XmlPathIn(string s)
        {
            int end = s.IndexOf(".xml", StringComparison.OrdinalIgnoreCase);
            if (end < 0) return "";
            int start = Math.Max(s.LastIndexOf(":\\", end, StringComparison.Ordinal), s.LastIndexOf(":/", end, StringComparison.Ordinal)) - 1;
            return start >= 0 ? s.Substring(start, end + 4 - start) : "";
        }

        // def-injection messages end in "(file.xml)" or contain it
        static string DefFileIn(string s)
        {
            int end = s.LastIndexOf(".xml)", StringComparison.OrdinalIgnoreCase);
            int start = end < 0 ? -1 : s.LastIndexOf('(', end);
            return start >= 0 ? s.Substring(start + 1, end + 4 - start - 1) : "";
        }
    }
}
