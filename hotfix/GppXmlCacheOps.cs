using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace LP17Hotfix
{
    static class GppXmlCacheOps
    {
        // Patch ops the performance patch cannot evaluate, so it refuses to build its XML cache.
        static readonly string[] XmlOnly =
        {
            "GiddyUp.PatchOperationDrawingOffset",
            "ilyvion.Laboratory.PatchOperationFindModById",
            "ToxinWarcasket.PatchOperationModActive",
            "AlphaArmoury.PatchOperation_ChargeQuestRewardCommonality",
            "AlphaCrafts.PatchOperation_Dropdown",
            "AlphaPrefabs.PatchOperationModOption",
            "AlphaPrefabs.PatchOperationNoCatalogMode",
            "ToxinWarcasket.PatchOperationSetting",
            "PersonaWeaponsUnbound.PatchOperation_UnlessPersonaCoreRecipeEnabled",
            "FairycoreCarillon.PatchOperation_CarillonMedieval",
            "BionicModularity.PatchOperation_SettingBool_UnBuyableModules",
            "BionicModularity.PatchOperation_SettingBool_AltJumpPack",
            "BionicModularity.PatchOperation_SettingBool_Omnimodularity",
        };

        // The ops that read a mod's settings also go in DecisionOps, or the cache key misses those settings and goes stale.
        static readonly string[] Decision =
        {
            "AlphaArmoury.PatchOperation_ChargeQuestRewardCommonality",
            "AlphaCrafts.PatchOperation_Dropdown",
            "AlphaPrefabs.PatchOperationModOption",
            "AlphaPrefabs.PatchOperationNoCatalogMode",
            "ToxinWarcasket.PatchOperationSetting",
            "PersonaWeaponsUnbound.PatchOperation_UnlessPersonaCoreRecipeEnabled",
            "FairycoreCarillon.PatchOperation_CarillonMedieval",
            "BionicModularity.PatchOperation_SettingBool_UnBuyableModules",
            "BionicModularity.PatchOperation_SettingBool_AltJumpPack",
            "BionicModularity.PatchOperation_SettingBool_Omnimodularity",
        };

        internal static void Apply() => Lp.Run("gpp-xml-cache-ops", () =>
        {
            Type t = AccessTools.TypeByName("GUIPerformancePatch.GppPatchCache");
            if (t == null) return "GppPatchCache not found";
            FieldInfo xmlField = AccessTools.Field(t, "XmlOnlyOps");
            FieldInfo decisionField = AccessTools.Field(t, "DecisionOps");
            if (xmlField == null) return "XmlOnlyOps not found";
            if (decisionField == null) return "DecisionOps not found";
            HashSet<string> xmlSet = xmlField.GetValue(null) as HashSet<string>;
            HashSet<string> decisionSet = decisionField.GetValue(null) as HashSet<string>;
            if (xmlSet == null) return "XmlOnlyOps is not a HashSet<string>";
            if (decisionSet == null) return "DecisionOps is not a HashSet<string>";

            // Both sets are read before anything is added, so a failure above leaves both untouched.
            int newXml = 0;
            foreach (string n in XmlOnly) if (xmlSet.Add(n)) newXml++;
            int newDecision = 0;
            foreach (string n in Decision) if (decisionSet.Add(n)) newDecision++;
            Lp.Note = "xmlonly " + XmlOnly.Length + " (+" + newXml + " new), decision " + Decision.Length + " (+" + newDecision + " new)";
            return null;
        });
    }

    // GetWeaponDamage looked GetDamageAmount up by name, which throws because ProjectileProperties has three overloads,
    // and the mod's catch then turned every ranged weapon's projectile damage into 0. None of the three takes one
    // argument either, so the mod's one-element Invoke array could never have worked. Calling the overload directly
    // drops the reflection; the mod's other branch read the private damageAmountBase with a public-only GetField, so it
    // never ran. Ranged weapons now contribute their real projectile damage, which melee-only values did not include.
    static class FactionGearAmbiguousDamage
    {
        internal static void Apply() => Lp.Run("factiongear-ambiguous-damage", () =>
        {
            MethodInfo target = AccessTools.Method("FactionGearCustomizer.FactionGearManager:GetWeaponDamage");
            if (target == null) return "FactionGearManager.GetWeaponDamage not found";
            new Harmony(Lp.Id).Patch(target, new HarmonyMethod(typeof(FactionGearAmbiguousDamage), nameof(Prefix)));
            return null;
        });

        static bool Prefix(ThingDef weaponDef, ref float __result)
        {
            float num = 0f;
            if (weaponDef != null)
            {
                if (weaponDef.IsRangedWeapon && weaponDef.Verbs != null && weaponDef.Verbs.Count > 0)
                {
                    VerbProperties verb = weaponDef.Verbs[0];
                    ThingDef projectile = verb == null ? null : verb.defaultProjectile;
                    if (projectile != null && projectile.projectile != null)
                        num = projectile.projectile.GetDamageAmount((Thing)null, (StringBuilder)null);
                }
                if (weaponDef.tools != null)
                    for (int i = 0; i < weaponDef.tools.Count; i++)
                    {
                        Tool tool = weaponDef.tools[i];
                        if (tool != null && tool.power > num) num = tool.power;
                    }
            }
            __result = num;
            return false;
        }
    }
}
