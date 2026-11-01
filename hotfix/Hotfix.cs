using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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
            BwmSettingsNoWorld.Apply(); // before Dub's Mint Menus reads its bill templates in its late-load action
            HideFlagsNoStub.Apply(); // before Dubs Performance Analyzer's GUI setup sets the flag
            YayoToolsCompat.Apply(); // before Show Me Your Tools patches at startup
            DefOfWrongType.Apply(); // before the game binds DefOfs
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
        }
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
            return (g as RimWorld.Designator_Place)?.PlacingDef;
        }

        static void Postfix(Command __instance, Gizmo other, ref bool __result)
        {
            if (!__result) return;
            BuildableDef a = Placed(__instance), b = Placed(other);
            if (a != null && b != null && a != b) __result = false;
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
