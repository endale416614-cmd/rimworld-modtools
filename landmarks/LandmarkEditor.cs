using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LandmarkEditor
{
    [StaticConstructorOnStartup]
    internal static class Startup
    {
        static Startup()
        {
            new Harmony("local.landmark.editor").PatchAll(typeof(Startup).Assembly);
        }
    }

    [HarmonyPatch(typeof(Tile), nameof(Tile.GetGizmos))]
    internal static class Patch_Tile_GetGizmos
    {
        [HarmonyPostfix]
        private static void Postfix(Tile __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!ModsConfig.OdysseyActive || __instance == null) return;
            __result = LandmarkGizmos(__instance).Concat(__result ?? Enumerable.Empty<Gizmo>());
        }

        private static IEnumerable<Gizmo> LandmarkGizmos(Tile t)
        {
            if (Find.World == null) yield break;

            yield return new Command_Action
            {
                defaultLabel = "Edit tile",
                defaultDesc = "Open the tile editor: set or remove this tile's landmark, and add or remove the features (deadlife geysers, caves, lakes and the rest) the map will generate with.",
                icon = BaseContent.NullOrBad(TexButton.Plus) ? BaseContent.BadTex : TexButton.Plus,
                action = () => Dialog_LandmarkEditor.OpenFor(t)
            };
        }
    }

    /// <summary>Set, replace and remove a tile's landmark and its features, keeping the tile's terrain in step.</summary>
    internal static class LandmarkOps
    {
        /// <summary>Puts a feature on the tile. Vanilla drops the ones it conflicts with and runs the worker hook.</summary>
        public static void AddFeature(Tile t, TileMutatorDef def)
        {
            if (t == null || def == null) return;
            if (t.Mutators != null && t.Mutators.Contains(def)) return;
            t.AddMutator(def);
            Refresh(t);
        }

        public static void RemoveFeature(Tile t, TileMutatorDef def)
        {
            if (t == null || def == null) return;
            t.RemoveMutator(def);
            Refresh(t);
        }


        public static void Set(Tile t, LandmarkDef def)
        {
            WorldLandmarks landmarks = Find.World?.landmarks;
            if (landmarks == null || t == null || def == null) return;
            RemoveOld(t);
            landmarks.AddLandmark(def, t.tile, t.Layer, true);
            Refresh(t);
        }

        public static void Clear(Tile t)
        {
            if (t == null) return;
            RemoveOld(t);
            Refresh(t);
        }

        private static void RemoveOld(Tile t)
        {
            WorldLandmarks landmarks = Find.World?.landmarks;
            Landmark current = t.Landmark;
            if (landmarks == null || current == null) return;

            LandmarkDef old = current.def;
            landmarks.RemoveLandmark(t.tile);
            if (old == null) return;

            foreach (MutatorChance mc in Chances(old))
            {
                // Known limit: only mutators listed in the old landmark def are removed, so a mutator the tile also got from world generation is removed too.
                if (mc?.mutator != null && t.Mutators != null && t.Mutators.Contains(mc.mutator))
                    t.RemoveMutator(mc.mutator);
            }
        }

        public static IEnumerable<MutatorChance> Chances(LandmarkDef def)
        {
            if (def.mutatorChances != null) foreach (MutatorChance mc in def.mutatorChances) yield return mc;
            if (def.comboLandmarkMutators != null) foreach (MutatorChance mc in def.comboLandmarkMutators) yield return mc;
        }

        private static void Refresh(Tile t)
        {
            LandmarkDef.ClearCache();
            WorldRenderer renderer = Find.World?.renderer;
            if (renderer != null) renderer.SetDirty<WorldDrawLayer_Landmarks>(t.Layer);
            ExpandableLandmarksUtility.Notify_WorldObjectsChanged();
        }
    }
}
