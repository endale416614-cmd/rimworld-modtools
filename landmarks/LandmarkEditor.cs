using System;
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
                defaultLabel = "Set landmark",
                defaultDesc = "Choose a landmark for this tile, replacing the current one. Mutator changes apply only to maps generated afterwards.",
                icon = Icon(TexButton.Plus),
                action = () => OpenSetMenu(t)
            };

            if (t.Landmark != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Remove landmark",
                    defaultDesc = "Remove the landmark from this tile and the mutators it added. Mutator changes apply only to maps generated afterwards.",
                    icon = Icon(TexButton.Delete),
                    action = () => ClearLandmark(t)
                };
            }
        }

        private static Texture2D Icon(Texture2D tex)
        {
            return BaseContent.NullOrBad(tex) ? BaseContent.BadTex : tex;
        }

        private static void OpenSetMenu(Tile t)
        {
            List<LandmarkDef> defs = DefDatabase<LandmarkDef>.AllDefsListForReading.ToList();
            defs.Sort((a, b) => string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.InvariantCultureIgnoreCase));

            // Several landmarks share one label (vanilla has four called "lake"), so the defName tells those apart.
            var shared = new Dictionary<string, int>();
            foreach (LandmarkDef def in defs)
            {
                string key = def.LabelCap.ToString();
                shared.TryGetValue(key, out int n);
                shared[key] = n + 1;
            }

            var options = new List<FloatMenuOption>();
            foreach (LandmarkDef def in defs)
            {
                LandmarkDef chosen = def;
                string label = chosen.LabelCap.ToString();
                if (shared[label] > 1) label += " (" + chosen.defName + ")";
                if (!chosen.IsValidTile(t.tile, t.Layer, false)) label += " (not normally valid here)";
                options.Add(new FloatMenuOption(label, () => SetLandmark(t, chosen)));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void SetLandmark(Tile t, LandmarkDef def)
        {
            WorldLandmarks landmarks = Find.World?.landmarks;
            if (landmarks == null) return;
            RemoveOldLandmark(t);
            landmarks.AddLandmark(def, t.tile, t.Layer, true);
            Refresh(t);
        }

        private static void ClearLandmark(Tile t)
        {
            RemoveOldLandmark(t);
            Refresh(t);
        }

        private static void RemoveOldLandmark(Tile t)
        {
            WorldLandmarks landmarks = Find.World?.landmarks;
            Landmark current = t.Landmark;
            if (landmarks == null || current == null) return;

            LandmarkDef old = current.def;
            landmarks.RemoveLandmark(t.tile);
            if (old?.mutatorChances == null) return;

            foreach (MutatorChance mc in old.mutatorChances)
            {
                // Known limit: only mutators listed in the old landmark def are removed, so a mutator the tile also got from world generation is removed too.
                if (mc?.mutator != null && t.Mutators != null && t.Mutators.Contains(mc.mutator))
                    t.RemoveMutator(mc.mutator);
            }
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
