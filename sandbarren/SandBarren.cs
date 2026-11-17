using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SandBarren
{
    [StaticConstructorOnStartup]
    internal static class Startup
    {
        static Startup()
        {
            Blocked.Build();
            new Harmony("local.sand.barren").PatchAll(typeof(Startup).Assembly);
        }
    }

    /// <summary>
    /// Which plants may not take root on sand. The sets are resolved once from the loaded defs, so plants
    /// and sand terrains that mods add are covered without a per-mod patch.
    /// </summary>
    internal static class Blocked
    {
        private static readonly HashSet<ThingDef> Plants = new HashSet<ThingDef>();
        private static readonly HashSet<TerrainDef> Sands = new HashSet<TerrainDef>();

        private static readonly string[] FungusWords = { "shroom", "mushroom", "fungus", "fungal", "mycel", "amanita", "toadstool" };
        private static readonly string[] FungusEndings = { "cap", "caps", "gill", "gills", "stool", "stools", "truffle", "truffles", "puff", "puffs", "puffer", "ceps" };

        /// <summary>Whole mods that are nothing but fungus, whose names carry no shared word.</summary>
        private static readonly string[] FungusPackages = { "vanillaexpanded.vplantsemushrooms" };

        public static void Build()
        {
            SelfCheck();

            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.plant == null) continue;
                if (IsTallGrass(d) || IsFungus(d)) Plants.Add(d);
            }

            foreach (TerrainDef t in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                if (IsSand(t)) Sands.Add(t);
            }

            Log.Message("[Barren Sand] " + Plants.Count + " plants blocked on " + Sands.Count + " sand terrains."
                + "\nPlants: " + string.Join(", ", Plants.Select(p => p.defName).OrderBy(s => s))
                + "\nTerrains: " + string.Join(", ", Sands.Select(s => s.defName).OrderBy(s => s)));
        }

        /// <summary>Asked first on the hot path, so a cell's terrain is only read for a plant that can be blocked.</summary>
        public static bool IsBlockedPlant(ThingDef plant)
        {
            return plant != null && Plants.Contains(plant);
        }

        public static bool IsSandTerrain(TerrainDef terrain)
        {
            return terrain != null && Sands.Contains(terrain);
        }

        private static bool IsTallGrass(ThingDef d)
        {
            return MatchesTallGrassName(d.defName, d.label);
        }

        private static bool IsFungus(ThingDef d)
        {
            // A tree named after the mushrooms on it is still a tree. cavePlant is no signal either:
            // seabed kelp and cave moss carry it.
            if (d.plant.IsTree || Key(d.defName).Contains("tree") || Key(d.label).Contains("tree")) return false;
            if (d.plant.harvestedThingDef != null
                && d.plant.harvestedThingDef.defName.IndexOf("Fungus", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (d.thingClass != null && d.thingClass.Name == "Plant_Boomshroom") return true;
            if (d.modContentPack != null && FungusPackages.Contains(d.modContentPack.PackageId)) return true;
            return MatchesFungusName(d.defName, d.label);
        }

        private static bool IsSand(TerrainDef t)
        {
            // Built floors are out, however they are named: play sand, zen sand, sand lion leather matting.
            if (!t.natural) return false;
            if (t.categoryType.ToString() == "Sand") return true;
            // Sand named by a mod with no category set. Fertile dune sand and sandstone floors are not sand here.
            if (t.fertility > 0.15f) return false;
            return MatchesSandName(t.defName, t.label);
        }

        /// <summary>The label carries the word too, which keeps the flowers of the Tall Grass mod out.</summary>
        public static bool MatchesTallGrassName(string defName, string label)
        {
            if (!Key(defName).Contains("tallgrass") && !Key(label).Contains("tallgrass")) return false;
            return Key(label).Contains("grass");
        }

        public static bool MatchesFungusName(string defName, string label)
        {
            return FungusName(Key(defName)) || FungusName(Key(label));
        }

        public static bool MatchesSandName(string defName, string label)
        {
            string k = Key(defName), l = Key(label);
            if (k.Contains("stone") || l.Contains("stone")) return false;
            return k.Contains("sand") || l.Contains("sand");
        }

        private static bool FungusName(string k)
        {
            if (k.Length == 0) return false;
            foreach (string w in FungusWords)
            {
                if (k.Contains(w)) return true;
            }
            foreach (string e in FungusEndings)
            {
                if (k.EndsWith(e)) return true;
            }
            return false;
        }

        /// <summary>Lower case letters only, so "mystic cap" and "MysticCap" compare the same.</summary>
        private static string Key(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            char[] buf = new char[s.Length];
            int n = 0;
            foreach (char c in s)
            {
                if (char.IsLetter(c)) buf[n++] = char.ToLowerInvariant(c);
            }
            return new string(buf, 0, n);
        }

        private static void SelfCheck()
        {
            int bad = 0;
            bad += Expect(MatchesFungusName("Plant_Psilocap", "psilocap"), true, "psilocap");
            bad += Expect(MatchesFungusName("VEE_DayBoomshroom", "light-resistant boomshroom"), true, "day boomshroom");
            bad += Expect(MatchesFungusName("Plant_Willowgill", "willowgill"), true, "willowgill");
            bad += Expect(MatchesFungusName("VPE_Plant_Healtruffle", "healtruffle"), true, "healtruffle");
            bad += Expect(MatchesFungusName("VPE_Plant_Toxpuff", "toxpuff"), true, "toxpuff");
            bad += Expect(MatchesFungusName("VPE_Plant_Bileceps", "bileceps"), true, "bileceps");
            bad += Expect(MatchesFungusName("Glowstool", "glowstool"), true, "glowstool");
            bad += Expect(MatchesFungusName("Plant_Nutrifungus", "nutrifungus"), true, "nutrifungus");
            bad += Expect(MatchesFungusName("VEE_Plant_MysticCap", "mystic cap"), true, "mystic cap");
            bad += Expect(MatchesFungusName("Plant_Grass", "grass"), false, "grass");
            bad += Expect(MatchesFungusName("Plant_SaguaroCactus", "saguaro cactus"), false, "saguaro cactus");
            bad += Expect(MatchesFungusName("Plant_Potato", "potato plant"), false, "potato");
            bad += Expect(MatchesTallGrassName("Plant_TallGrass", "tall grass"), true, "tall grass");
            bad += Expect(MatchesTallGrassName("VCE_TallGrass", "tall grass"), true, "VCE tall grass");
            bad += Expect(MatchesTallGrassName("Plant_YellowTallGrass", "yellow tall grass"), true, "yellow tall grass");
            bad += Expect(MatchesTallGrassName("TallGrass_Sunflower", "sunflowers"), false, "Tall Grass mod sunflowers");
            bad += Expect(MatchesTallGrassName("RG_Plant_TallToxiGrass", "tall toxigrass"), false, "tall toxigrass");
            bad += Expect(MatchesTallGrassName("Plant_Grass", "grass"), false, "short grass");
            bad += Expect(MatchesSandName("Sand", "sand"), true, "sand");
            bad += Expect(MatchesSandName("SoftSand", "soft sand"), true, "soft sand");
            bad += Expect(MatchesSandName("TileSandstone", "sandstone tile"), false, "sandstone tile");
            bad += Expect(MatchesSandName("Soil", "soil"), false, "soil");
            if (bad > 0) Log.Error("[Barren Sand] " + bad + " name rule self-checks failed; the blocked sets may be wrong.");
        }

        private static int Expect(bool got, bool want, string what)
        {
            if (got == want) return 0;
            Log.Error("[Barren Sand] self-check failed: " + what + " gave " + got + ", wanted " + want);
            return 1;
        }
    }

    /// <summary>The one gate every spawn, sow and map generation path goes through.</summary>
    [HarmonyPatch]
    internal static class Patch_PlantUtility_CanEverPlantAt
    {
        private static MethodBase TargetMethod()
        {
            return typeof(PlantUtility).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "CanEverPlantAt" && m.ReturnType == typeof(AcceptanceReport));
        }

        [HarmonyPrefix]
        private static bool Prefix(ThingDef __0, IntVec3 __1, Map __2, ref Thing __3, ref AcceptanceReport __result)
        {
            if (!Blocked.IsBlockedPlant(__0)) return true;
            if (__2 == null || !__1.InBounds(__2)) return true;
            if (!Blocked.IsSandTerrain(__1.GetTerrain(__2))) return true;
            __3 = null;
            __result = false;
            return false;
        }
    }

    /// <summary>Clears what grew on sand before this mod, and whatever map generation planted there anyway.</summary>
    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    internal static class Patch_Map_FinalizeInit
    {
        [HarmonyPostfix]
        private static void Postfix(Map __instance)
        {
            if (__instance == null || __instance.listerThings == null) return;

            List<Thing> plants = new List<Thing>(__instance.listerThings.ThingsInGroup(ThingRequestGroup.Plant));
            int n = 0;
            foreach (Thing t in plants)
            {
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (!Blocked.IsBlockedPlant(t.def)) continue;
                if (!Blocked.IsSandTerrain(t.Position.GetTerrain(__instance))) continue;
                t.Destroy(DestroyMode.Vanish);
                n++;
            }
            if (n > 0) Log.Message("[Barren Sand] removed " + n + " tall grass and fungus plants standing on sand.");
        }
    }
}
