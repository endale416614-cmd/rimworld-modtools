using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hitchfix
{
    public class HitchfixMod : Mod
    {
        // Translations are injected into defs before any [StaticConstructorOnStartup] class runs, so the patch has to go on
        // in the Mod constructor to be in place in time.
        public HitchfixMod(ModContentPack content) : base(content)
        {
            MissingDefInjections.Apply();
        }
    }

    static class MissingDefInjections
    {
        // A mod ships English text for every def it might ever meet, including defs that belong to mods this list does not
        // run. The game injects translations with errorOnDefNotFound set, and that flag gates one thing only: a line added to
        // DefInjectionPackage.loadErrors reading "Found no <type> named <def> to match <path>". The injection itself returns
        // false either way, so nothing is broken and nothing is lost. LoadedLanguage.InjectIntoData_AfterImpliedDefs then
        // sums those lists and warns "Translation data for language English has N errors", which sends players hunting for a
        // fault that is not there. Those entries are dropped before the sum is taken, and counted in one line instead.
        //
        // Every other kind of translation error (duplicate keys, bad paths, untranslatable fields, malformed files) is left
        // alone and still raises the warning. If the game ever reworks that message, nothing matches, nothing is removed and
        // the warning comes back unchanged.
        const string Opens = "Found no ";
        const string Names = " to match ";
        static int dropped;

        internal static void Apply() => Fix.Run("missing-def-injections", () =>
        {
            MethodInfo inject = AccessTools.Method(typeof(DefInjectionPackage), nameof(DefInjectionPackage.InjectIntoDefs));
            if (inject == null) return "DefInjectionPackage.InjectIntoDefs not found";
            MethodInfo sum = AccessTools.Method(typeof(LoadedLanguage), nameof(LoadedLanguage.InjectIntoData_AfterImpliedDefs));
            if (sum == null) return "LoadedLanguage.InjectIntoData_AfterImpliedDefs not found";
            Harmony harmony = new Harmony(Fix.Id);
            harmony.Patch(inject, postfix: new HarmonyMethod(typeof(MissingDefInjections), nameof(Postfix)));
            // Reported from the method that sums the lists, because a queued callback runs before injection has happened.
            harmony.Patch(sum, postfix: new HarmonyMethod(typeof(MissingDefInjections), nameof(Report)));
            return null;
        });

        // InjectIntoDefs clears loadErrors on entry, so this sees exactly what the run it follows added. The caller reads
        // loadErrors.Count immediately after each call, which is why the pass has to happen here and not later.
        static void Postfix(DefInjectionPackage __instance)
        {
            List<string> errors = __instance.loadErrors;
            if (errors == null) return;
            for (int i = errors.Count - 1; i >= 0; i--)
            {
                string line = errors[i];
                if (line == null) continue;
                if (!line.StartsWith(Opens, StringComparison.Ordinal)) continue;
                if (line.IndexOf(Names, StringComparison.Ordinal) < 0) continue;
                errors.RemoveAt(i);
                dropped++;
            }
        }

        internal static void Report()
        {
            if (dropped == 0) return;
            Log.Message("[Hitchfix] missing-def-injections: " + dropped + " translation entries name a def this mod list does not load. They are not faults and are no longer counted as errors.");
        }
    }

    [StaticConstructorOnStartup]
    static class HitchfixStartup
    {
        // The def database is filled after the Mod constructor runs, so this is the earliest point the defs can be edited.
        static HitchfixStartup() => DuplicateStyleableComps.Apply();
    }

    static class DuplicateStyleableComps
    {
        // Several mods list CompProperties_Styleable on the same ThingDef. CompStyleable holds one sourcePrecept and styleDef,
        // so a second instance registers the same load ID twice on every save load.
        internal static void Apply() => Fix.Run("duplicate-styleable-comps", () =>
        {
            string failed = SelfCheck();
            if (failed != null) return failed;
            int removed = 0;
            int defs = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                int n = RemoveDuplicates(def.comps);
                if (n == 0) continue;
                removed += n;
                defs++;
            }
            Fix.Note = removed == 0 ? "no duplicates found" : "removed " + removed + " duplicate CompStyleable from " + defs + " defs";
            return null;
        });

        // Keeps the first CompStyleable entry and drops the rest. Matches on compClass, not the XML class name.
        internal static int RemoveDuplicates(List<CompProperties> comps)
        {
            if (comps == null) return 0;
            int removed = 0;
            bool kept = false;
            for (int i = 0; i < comps.Count; i++)
            {
                CompProperties comp = comps[i];
                if (comp == null || comp.compClass != typeof(CompStyleable)) continue;
                if (!kept) { kept = true; continue; }
                comps.RemoveAt(i);
                i--;
                removed++;
            }
            return removed;
        }

        static string SelfCheck()
        {
            List<CompProperties> comps = new List<CompProperties>
            {
                new CompProperties_Styleable(),
                new CompProperties(typeof(CompQuality)),
                new CompProperties_Styleable(),
            };
            if (RemoveDuplicates(comps) != 1) return "self-check: removed count wrong";
            if (comps.Count != 2) return "self-check: wrong entry count after removal";
            int styleable = 0;
            bool unrelated = false;
            foreach (CompProperties c in comps)
            {
                if (c.compClass == typeof(CompStyleable)) styleable++;
                if (c.compClass == typeof(CompQuality)) unrelated = true;
            }
            if (styleable != 1) return "self-check: CompStyleable not kept once";
            if (!unrelated) return "self-check: unrelated comp dropped";
            if (RemoveDuplicates(comps) != 0) return "self-check: second pass not idempotent";
            return null;
        }
    }    static class Fix
    {
        internal const string Id = "local.hitchfix";

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
            Log.Message("[Hitchfix] " + fix + ": " + (skip == null ? "applied" + (Note == null ? "" : " (" + Note + ")") : "skipped (" + skip + ")"));
        }
    }
}
