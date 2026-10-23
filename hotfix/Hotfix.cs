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
            YartLayerSeparation.Apply();
            TranslationDump.Apply();
        }
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
