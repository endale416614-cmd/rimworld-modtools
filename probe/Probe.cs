// Test-only build of LP17 Hotfix (dotnet build -p:Probe=true). Env LP17_PROBE selects the scan:
//   static : JIT every active mod method, check Harmony targets, dump the patch table to LP17_PROBE_OUT.
//   dump   : write every faction, pawn kind, weapon and apparel def to LP17_PROBE_OUT.<table>.tsv.
//   gear   : load LP17_PROBE_SAVE, generate every faction's pawn kinds and visitor group, write what they wear and carry to LP17_PROBE_OUT.gear.tsv.
//   soak   : load LP17_PROBE_SAVE, autosave off, open UI tabs, build every def, generate pawn kinds, spawn things,
//            fire incidents and quests, then run ticks. The game is killed afterwards and never saved.
//   scan   : load LP17_PROBE_SAVE, write every buildable and why the architect menu shows it or not to LP17_PROBE_OUT.architect.tsv,
//            then time every Harmony patch and game component over LP17_PROBE_TICKS ticks to LP17_PROBE_OUT.perf.tsv.
//   autoresearch : load LP17_PROBE_SAVE, click YART QoL's auto research button and run research out in memory, checking each pick.
//            Writes LP17_PROBE_OUT.picks.tsv and .remaining.tsv. Its only save is a temporary one for the reload check, deleted right after.
//   hemogentap : load LP17_PROBE_SAVE, check the Hemogen Tap research project gates its fabrication bench recipe and unlocks the implant, give a colonist the implant,
//            check its gizmo toggle switches the tap off and on, check one day's tick drops one hemogen pack and caps blood loss at 0.44 and that a second tick does nothing.
//   bs9000gif : load LP17_PROBE_SAVE and stage a scene for a screen recording on a clear patch of the map, in three cuts marked by the log lines "[PROBE] cut1 start" to "[PROBE] cut3 end":
//            a face zoom on a colonist, that colonist walking in to tap and walking out, a doctor taking the dropped hemogen pack to transfuse a bleeding patient in a bed.
//            It only changes the loaded game in memory (time of day, weather, camera, UI, a bed, the colonists' places and jobs) and saves nothing.
//   newgame : load LP17_PROBE_SAVE, check the vanilla female body, generate classic ideos and record history before game start, then repeat with those fixes off as a control.
//   select : load LP17_PROBE_SAVE, send double clicks through the engine's input path on furniture, walls, blueprints, animals and colonists, report what got selected and which
//            commands the gizmo grid drew and click one, check short hashes, an egg hatching after its parent was discarded and quest drop spots, repeat with the fixes off as a
//            control, then make a temporary save (deleted right after).
//   conflicts : load LP17_PROBE_SAVE, check the mod conflict fixes (nutrient paste dispenser toils, gear row hiding, flare shield and Genetron, turret cast source),
//            then unpatch them and repeat as a control that must show the opposite. It changes settings and spawns things in memory only, undoes them, and saves nothing.
// Every mode also writes exceptions that were thrown, caught or not, to LP17_PROBE_OUT.exceptions.tsv.
// Every line starts with [PROBE so harness/proberun.ps1 can attribute errors to the step before them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LP17Hotfix
{
    [StaticConstructorOnStartup]
    public static class Probe
    {
        static readonly string mode = Environment.GetEnvironmentVariable("LP17_PROBE");
        static readonly string save = Environment.GetEnvironmentVariable("LP17_PROBE_SAVE");
        static readonly string outPath = Environment.GetEnvironmentVariable("LP17_PROBE_OUT");

        static Probe()
        {
            if (string.IsNullOrEmpty(mode)) return;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                string fail = CompressedThingsByName.SelfTest();
                Log.Message(fail == null ? "[PROBE] ok compressed-things-by-name self-test" : "[PROBE] FAIL compressed-things-by-name self-test: " + fail);
            });
            AppDomain.CurrentDomain.FirstChanceException += (s, e) => CountException(e.Exception);
            if (mode == "static" || mode == "dump") new Harmony("local.lp17.probe").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Probe), nameof(MenuFrame)));
            else if (mode == "soak" || mode == "gear" || mode == "scan" || mode == "autoresearch" || mode == "hemogentap" || mode == "bs9000gif" || mode == "newgame" || mode == "select" || mode == "conflicts")
            {
                Harmony h = new Harmony("local.lp17.probe");
                h.Patch(AccessTools.Method(typeof(Autosaver), "AutosaverTick"), prefix: new HarmonyMethod(typeof(Probe), nameof(No)));
                h.Patch(AccessTools.Method(typeof(Autosaver), "DoAutosave"), prefix: new HarmonyMethod(typeof(Probe), nameof(No)));
                h.Patch(AccessTools.Method(typeof(TickManager), "TickManagerUpdate"), postfix: new HarmonyMethod(typeof(Probe), nameof(Frame)));
                // A save whose mod list differs from the active one (the recording runs a short list plus this mod) would wait on a confirm dialog; skip it.
                h.Patch(AccessTools.Method(typeof(ScribeMetaHeaderUtility), nameof(ScribeMetaHeaderUtility.TryCreateDialogsForVersionMismatchWarnings)), prefix: new HarmonyMethod(typeof(Probe), nameof(NoMismatchDialog)));
                h.Patch(AccessTools.Method(typeof(PawnUIOverlay), nameof(PawnUIOverlay.DrawPawnGUIOverlay)), prefix: new HarmonyMethod(typeof(Probe), nameof(NoLabelsWhileFilming))); // name labels draw even in screenshot mode
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    Log.Message("[PROBE] loading " + save);
                    if (string.IsNullOrEmpty(save)) LongEventHandler.QueueLongEvent(() => { Root_Play.SetupForQuickTestPlay(); PageUtility.InitGameStart(); }, "GeneratingMap", true, null);
                    else GameDataSaveLoader.LoadGame(save);
                });
            }
        }

        static bool No() => false;

        static bool NoMismatchDialog(ref bool __result) { __result = false; return false; }

        static bool NoLabelsWhileFilming() => !filming;

        // ---- exceptions, shown in the log or not ----

        sealed class Thrown { public string phase, type, where, message, stack; public int count; }

        static readonly Dictionary<string, Thrown> thrown = new Dictionary<string, Thrown>();

        [ThreadStatic] static bool inCount;

        static void CountException(Exception e)
        {
            if (inCount) return;
            inCount = true;
            try
            {
                string phase = Current.ProgramState == ProgramState.Playing ? "play" : "load";
                string where = (e.StackTrace ?? "").Split('\n')[0].Trim();
                string key = phase + "|" + e.GetType().FullName + "|" + where;
                lock (thrown)
                {
                    if (!thrown.TryGetValue(key, out Thrown t))
                        thrown[key] = t = new Thrown { phase = phase, type = e.GetType().Name, where = where, message = First(e.Message), stack = string.Join(" <- ", Environment.StackTrace.Split('\n').Skip(3).Take(14).Select(x => x.Trim().Replace("at ", ""))) };
                    t.count++;
                }
            }
            catch { }
            finally { inCount = false; }
        }

        static void Finish()
        {
            if (!string.IsNullOrEmpty(outPath))
                lock (thrown)
                    Table("exceptions", new[] { "phase", "count", "type", "where", "message", "stack" }, thrown.Values.OrderByDescending(t => t.count).Select(t => new object[] { t.phase, t.count, t.type, t.where, t.message, t.stack }));
            Log.Message("[PROBE] done");
        }

        static int menuFrames;

        // Some mods patch in a late long event, so wait until the main menu has run a while.
        static void MenuFrame()
        {
            if (LongEventHandler.AnyEventNowOrWaiting || ++menuFrames != 600) return;
            if (mode == "dump") Dump();
            else StaticScan();
        }

        // ---- def dump ----

        static string Cell(object o) => (o?.ToString() ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        static string Join<T>(IEnumerable<T> items) => items == null ? "" : string.Join(",", items);

        static void Table(string name, string[] head, IEnumerable<object[]> rows)
        {
            using (StreamWriter w = new StreamWriter(outPath + "." + name + ".tsv"))
            {
                w.WriteLine(string.Join("\t", head));
                foreach (object[] r in rows) w.WriteLine(string.Join("\t", r.Select(Cell)));
            }
        }

        static string Options(List<PawnGenOption> list) => list == null ? "" : string.Join(",", list.Select(o => o.kind?.defName + "*" + o.selectionWeight));

        static string Groups(FactionDef f) => f.pawnGroupMakers == null ? "" : string.Join(";", f.pawnGroupMakers.Select(g => g.kindDef?.defName + "[o=" + Options(g.options) + " t=" + Options(g.traders) + " c=" + Options(g.carriers) + " g=" + Options(g.guards) + "]"));

        static string Research(ThingDef d) => Join(d.recipeMaker?.researchPrerequisites?.Select(r => r.defName) ?? (d.recipeMaker?.researchPrerequisite != null ? new[] { d.recipeMaker.researchPrerequisite.defName } : null));

        static string WornBodies(ThingDef d)
        {
            string path = d.apparel?.wornGraphicPath;
            if (path.NullOrEmpty()) return "none";
            if (d.apparel.LastLayer == ApparelLayerDefOf.Overhead || d.apparel.LastLayer == ApparelLayerDefOf.EyeCover) return "any";
            string[] bodies = { "Male", "Female", "Thin", "Hulk", "Fat" };
            return Join(bodies.Where(b => ContentFinder<UnityEngine.Texture2D>.Get(path + "_" + b + "_south", false) != null));
        }

        static void Dump()
        {
            try
            {
                Table("factions", new[] { "defName", "label", "mod", "tech", "hidden", "humanlike", "permanentEnemy", "isPlayer", "categoryTag", "groups" },
                    DefDatabase<FactionDef>.AllDefs.Select(f => new object[] { f.defName, f.label, f.modContentPack?.PackageIdPlayerFacing, f.techLevel, f.hidden, f.humanlikeFaction, f.permanentEnemy, f.isPlayer, f.categoryTag, Groups(f) }));
                Table("kinds", new[] { "defName", "label", "mod", "race", "power", "apparelMoney", "weaponMoney", "apparelTags", "apparelDisallow", "weaponTags", "fighter", "leader", "trader", "fixedGender", "required", "specific", "defaultFaction" },
                    DefDatabase<PawnKindDef>.AllDefs.Select(k => new object[] { k.defName, k.label, k.modContentPack?.PackageIdPlayerFacing, k.race?.defName, k.combatPower, k.apparelMoney, k.weaponMoney, Join(k.apparelTags), Join(k.apparelDisallowTags), Join(k.weaponTags), k.isFighter, k.factionLeader, k.trader, k.fixedGender, Join(k.apparelRequired?.Select(t => t.defName)), Join(k.specificApparelRequirements?.Select(t => t.ApparelDef?.defName)), k.defaultFactionDef?.defName }));
                Table("apparel", new[] { "defName", "label", "mod", "tech", "apparelTags", "tradeTags", "layers", "parts", "gender", "stuff", "sharp", "blunt", "cold", "value", "categories", "research", "bodies", "graphic", "stages", "desc" },
                    DefDatabase<ThingDef>.AllDefs.Where(d => d.IsApparel).Select(d => new object[] { d.defName, d.label, d.modContentPack?.PackageIdPlayerFacing, d.techLevel, Join(d.apparel.tags), Join(d.tradeTags), Join(d.apparel.layers?.Select(l => l.defName)), Join(d.apparel.bodyPartGroups?.Select(g => g.defName)), d.apparel.gender, d.MadeFromStuff, d.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp), d.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt), d.GetStatValueAbstract(StatDefOf.Insulation_Cold), d.BaseMarketValue, Join(d.thingCategories?.Select(c => c.defName)), Research(d), WornBodies(d), d.apparel.wornGraphicPath, d.apparel.developmentalStageFilter, d.description?.Length > 110 ? d.description.Substring(0, 110) : d.description }));
                Table("weapons", new[] { "defName", "label", "mod", "tech", "weaponTags", "tradeTags", "classes", "kind", "value", "destroyOnDrop", "stuff", "categories", "research", "range", "damage", "burst", "desc" },
                    DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon && d.equipmentType == EquipmentType.Primary).Select(d =>
                    {
                        VerbProperties v = d.Verbs?.FirstOrDefault(x => x.isPrimary) ?? d.Verbs?.FirstOrDefault();
                        return new object[] { d.defName, d.label, d.modContentPack?.PackageIdPlayerFacing, d.techLevel, Join(d.weaponTags), Join(d.tradeTags), Join(d.weaponClasses?.Select(c => c.defName)), d.IsRangedWeapon ? "ranged" : "melee", d.BaseMarketValue, d.destroyOnDrop, d.MadeFromStuff, Join(d.thingCategories?.Select(c => c.defName)), Research(d), v?.range, v?.defaultProjectile?.projectile?.GetDamageAmount((Thing)null), v?.burstShotCount, d.description?.Length > 110 ? d.description.Substring(0, 110) : d.description };
                    }));
            }
            catch (Exception e) { Log.Error("[PROBE] dump failed: " + e); }
            Finish();
        }

        // ---- static scan ----

        static IEnumerable<(ModContentPack mod, Assembly asm)> ModAssemblies()
        {
            foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                foreach (Assembly a in m.assemblies.loadedAssemblies)
                    if (a.GetName().Name != "0Harmony") yield return (m, a);
        }

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        static void StaticScan()
        {
            try { JitAll(); } catch (Exception e) { Log.Error("[PROBE] jit failed: " + e); }
            try { HarmonyTargets(); } catch (Exception e) { Log.Error("[PROBE] harmony failed: " + e); }
            try { PatchTable(); } catch (Exception e) { Log.Error("[PROBE] table failed: " + e); }
            try { CompProps(); } catch (Exception e) { Log.Error("[PROBE] comps failed: " + e); }
            Finish();
        }

        static void JitAll()
        {
            int ok = 0, bad = 0, skipped = 0;
            Stopwatch sw = Stopwatch.StartNew();
            foreach ((ModContentPack mod, Assembly asm) in ModAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                    foreach (string msg in e.LoaderExceptions.Select(x => x.Message).Distinct())
                        Log.Message($"[PROBE-TYPELOAD] {mod.PackageIdPlayerFacing} | {asm.GetName().Name} | {First(msg)}");
                }
                foreach (Type t in types)
                {
                    MethodBase[] methods;
                    try { methods = t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)).ToArray(); }
                    catch (Exception e)
                    {
                        Log.Message($"[PROBE-TYPE] {mod.PackageIdPlayerFacing} | {asm.GetName().Name} | {t.FullName} | {e.GetType().Name} | {First(e.Message)}");
                        continue;
                    }
                    foreach (MethodBase m in methods)
                    {
                        try { if (m.IsAbstract || m.GetMethodBody() == null) continue; }
                        catch { continue; }
                        try
                        {
                            if (!m.ContainsGenericParameters) RuntimeHelpers.PrepareMethod(m.MethodHandle);
                            else
                            {
                                // Shared generic code: compile once for reference types, which is what most calls use.
                                int n = t.GetGenericArguments().Length + (m.IsGenericMethod ? m.GetGenericArguments().Length : 0);
                                RuntimeHelpers.PrepareMethod(m.MethodHandle, Enumerable.Repeat(typeof(object).TypeHandle, n).ToArray());
                            }
                            ok++;
                        }
                        catch (ArgumentException) { skipped++; } // the constraints reject object
                        catch (InvalidOperationException) { skipped++; }
                        catch (Exception e)
                        {
                            bad++;
                            Log.Message($"[PROBE-JIT] {mod.PackageIdPlayerFacing} | {asm.GetName().Name} | {t.FullName}::{m.Name} | {e.GetType().Name} | {First(e.Message)}");
                        }
                    }
                }
            }
            Log.Message($"[PROBE] jit: {ok} ok, {bad} failed, {skipped} generic skipped, {sw.Elapsed.TotalSeconds:F0}s");
        }

        static string First(string s) => s.Split('\n')[0].Trim();

        // A comp's typed Props getter casts its CompProperties, so a def with the wrong properties class throws when read.
        static void CompProps()
        {
            int bad = 0;
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.comps == null) continue;
                foreach (CompProperties c in d.comps)
                {
                    if (c?.compClass == null) continue;
                    foreach (PropertyInfo pi in c.compClass.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (!typeof(CompProperties).IsAssignableFrom(pi.PropertyType) || pi.PropertyType.IsInstanceOfType(c)) continue;
                        bad++;
                        Log.Message($"[PROBE-COMP] {d.modContentPack?.PackageIdPlayerFacing} | {d.defName} | {c.compClass.FullName}.{pi.Name} wants {pi.PropertyType.FullName} | has {c.GetType().FullName}");
                    }
                }
            }
            Log.Message($"[PROBE] comp props: {bad} mismatches");
        }

        static void HarmonyTargets()
        {
            int ok = 0, missing = 0, unapplied = 0;
            foreach ((ModContentPack mod, Assembly asm) in ModAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (Type t in types)
                {
                    List<HarmonyMethod> own;
                    try { own = HarmonyMethodExtensions.GetFromType(t); } catch { continue; }
                    bool custom = t.GetMethods(All).Any(m => m.Name == "TargetMethod" || m.Name == "TargetMethods" || m.Name == "Prepare"
                        || m.GetCustomAttributes(typeof(HarmonyTargetMethod), false).Any() || m.GetCustomAttributes(typeof(HarmonyTargetMethods), false).Any());
                    if (own != null && own.Count > 0 && !custom)
                        Check(mod, t, null, HarmonyMethod.Merge(own), ref ok, ref missing, ref unapplied);
                    foreach (MethodInfo m in t.GetMethods(All))
                    {
                        List<HarmonyMethod> mine;
                        try { mine = HarmonyMethodExtensions.GetFromMethod(m); } catch { continue; }
                        if (mine == null || !mine.Any(x => x.declaringType != null || x.methodName != null)) continue;
                        Check(mod, t, m, HarmonyMethod.Merge((own ?? new List<HarmonyMethod>()).Concat(mine).ToList()), ref ok, ref missing, ref unapplied);
                    }
                }
            }
            Log.Message($"[PROBE] harmony attribute targets: {ok} applied, {missing} missing, {unapplied} found but not patched");
        }

        static void Check(ModContentPack mod, Type t, MethodInfo patch, HarmonyMethod hm, ref int ok, ref int missing, ref int unapplied)
        {
            if (hm.declaringType == null) return;
            MethodBase target = null;
            try
            {
                MethodType kind = hm.methodType ?? MethodType.Normal;
                if (kind == MethodType.Normal) target = hm.methodName == null ? null : AccessTools.Method(hm.declaringType, hm.methodName, hm.argumentTypes);
                else if (kind == MethodType.Getter) target = AccessTools.PropertyGetter(hm.declaringType, hm.methodName);
                else if (kind == MethodType.Setter) target = AccessTools.PropertySetter(hm.declaringType, hm.methodName);
                else if (kind == MethodType.Constructor) target = AccessTools.Constructor(hm.declaringType, hm.argumentTypes);
                else if (kind == MethodType.StaticConstructor) target = AccessTools.Constructor(hm.declaringType, hm.argumentTypes, true);
                else return;
            }
            catch (Exception e)
            {
                // Overloads and no argument list: count it as applied if any overload carries a patch from this assembly.
                bool any = AccessTools.GetDeclaredMethods(hm.declaringType).Where(x => x.Name == hm.methodName).Any(x =>
                {
                    Patches pi = Harmony.GetPatchInfo(x);
                    return pi != null && pi.Prefixes.Concat(pi.Postfixes).Concat(pi.Transpilers).Concat(pi.Finalizers).Any(p => p.PatchMethod.Module.Assembly == t.Assembly);
                });
                if (any) ok++;
                else Log.Message($"[PROBE-HARMONY] {mod.PackageIdPlayerFacing} | AMBIGUOUS | {t.FullName} | {hm.declaringType.FullName}.{hm.methodName} | {First(e.Message)}");
                return;
            }
            if (hm.methodName == null && (hm.methodType ?? MethodType.Normal) == MethodType.Normal) return;
            if (target == null)
            {
                missing++;
                Log.Message($"[PROBE-HARMONY] {mod.PackageIdPlayerFacing} | MISSING | {t.FullName} | {hm.declaringType.FullName}.{hm.methodName}({Args(hm)}) {hm.methodType}");
                return;
            }
            Patches info = Harmony.GetPatchInfo(target);
            bool applied = info != null && info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Any(p => p.PatchMethod.DeclaringType == t || p.PatchMethod.DeclaringType.DeclaringType == t || p.owner.Length > 0 && p.PatchMethod.Module.Assembly == t.Assembly);
            if (applied) ok++;
            else
            {
                unapplied++;
                Log.Message($"[PROBE-HARMONY] {mod.PackageIdPlayerFacing} | UNAPPLIED | {t.FullName} | {hm.declaringType.FullName}.{hm.methodName}");
            }
        }

        static string Args(HarmonyMethod hm) => hm.argumentTypes == null ? "any" : string.Join(",", hm.argumentTypes.Select(a => a?.Name));

        static void PatchTable()
        {
            using StreamWriter w = new StreamWriter(outPath);
            w.WriteLine("target\tkind\towner\tpriority\tpatch\treturnsBool");
            foreach (MethodBase m in Harmony.GetAllPatchedMethods())
            {
                Patches p = Harmony.GetPatchInfo(m);
                if (p == null) continue;
                string target = m.DeclaringType?.FullName + "::" + m.Name + "(" + string.Join(",", m.GetParameters().Select(x => x.ParameterType.Name)) + ")";
                void Row(string kind, IEnumerable<Patch> list)
                {
                    foreach (Patch x in list)
                        w.WriteLine($"{target}\t{kind}\t{x.owner}\t{x.priority}\t{x.PatchMethod.DeclaringType?.FullName}::{x.PatchMethod.Name}\t{x.PatchMethod.ReturnType == typeof(bool)}");
                }
                Row("prefix", p.Prefixes); Row("postfix", p.Postfixes); Row("transpiler", p.Transpilers); Row("finalizer", p.Finalizers);
            }
        }

        // ---- soak ----

        static IEnumerator<int> run;
        static readonly HashSet<string> skip = new HashSet<string>((Environment.GetEnvironmentVariable("LP17_PROBE_SKIP") ?? "").Split(','));

        static void Frame()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            Log.ResetMessageCount();
            foreach (Window w in Find.WindowStack.Windows.ToList())
                if (w.forcePause || w is Dialog_MessageBox || filming) w.Close(false);
            if (run == null) { Log.Message("[PROBE] playing"); run = (mode == "gear" ? Gear() : mode == "scan" ? Scan() : mode == "autoresearch" ? AutoResearchTest() : mode == "hemogentap" ? BS9000Test() : mode == "bs9000gif" ? Bs9000Gif() : mode == "newgame" ? NewGameTest() : mode == "select" ? SelectTest() : mode == "conflicts" ? ConflictsTest() : Soak()).GetEnumerator(); }
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                while (sw.ElapsedMilliseconds < 80)
                {
                    if (!run.MoveNext()) { Finish(); run = Idle().GetEnumerator(); return; }
                    if (run.Current > 0) return; // yield 1: end the frame so the UI draws
                }
            }
            catch (Exception e) { Log.Error("[PROBE] step threw: " + e); run = Idle().GetEnumerator(); }
        }

        static IEnumerable<int> Idle() { while (true) yield return 1; }

        // ---- selection: double clicks through the engine's input path ----

        static readonly Type inputState = AccessTools.TypeByName("Waylith.Input.InputState");
        static readonly Type inputSystem = AccessTools.TypeByName("UnityEngine.InputSystem.InputSystem");
        static readonly Queue<Action> injected = new Queue<Action>();
        static readonly List<string> gridErrors = new List<string>();
        static readonly FieldInfo gizmoGroups = AccessTools.Field(typeof(GizmoGridDrawer), "gizmoGroups");

        // Both GLFW callbacks run right after InputState.BeginFrame, so this is where a real click would arrive.
        static void InjectNow() { while (injected.Count > 0) injected.Dequeue()(); }

        static Exception GridFinalizer(Exception __exception)
        {
            if (__exception != null && gridErrors.Count < 400) gridErrors.Add(__exception.GetType().Name + " at " + (__exception.StackTrace ?? "").Split('\n')[0].Trim());
            return __exception;
        }

        static void MoveMouse(UnityEngine.Vector2 screen) => injected.Enqueue(() =>
        {
            AccessTools.Method(inputState, "OnMouseMove").Invoke(null, new object[] { new System.Numerics.Vector2(screen.x, UnityEngine.Screen.height - screen.y) });
            AccessTools.Method(inputSystem, "QueueMouseMove").Invoke(null, new object[] { screen });
        });

        static object Left(ParameterInfo p) => p.ParameterType.IsEnum ? Enum.ToObject(p.ParameterType, 0) : Convert.ChangeType(0, p.ParameterType);

        static void MouseButton(UnityEngine.Vector2 screen, bool down) => injected.Enqueue(() =>
        {
            MethodInfo state = AccessTools.Method(inputState, "OnMouseButton"), queue = AccessTools.Method(inputSystem, "QueueMouseButton");
            state.Invoke(null, new object[] { Left(state.GetParameters()[0]), down, new System.Numerics.Vector2(screen.x, UnityEngine.Screen.height - screen.y) });
            queue.Invoke(null, new object[] { Left(queue.GetParameters()[0]), down, screen });
        });

        static readonly Dictionary<string, UnityEngine.Vector2> commandsDrawn = new Dictionary<string, UnityEngine.Vector2>();
        static readonly Dictionary<string, string> commandOwners = new Dictionary<string, string>();
        static readonly List<string> windowsAdded = new List<string>();

        static void CommandDrawn(Command __instance, UnityEngine.Vector2 __0)
        {
            try
            {
                commandsDrawn[__instance.LabelCap] = __0;
                commandOwners[__instance.LabelCap] = (__instance as Command_Action)?.action?.Method.DeclaringType?.FullName ?? __instance.GetType().FullName;
            }
            catch (Exception e) { commandsDrawn["label threw " + e.GetType().Name] = __0; }
        }

        static void WindowAdded(Window window) => windowsAdded.Add(window.GetType().Name);

        // The code of designators-group-by-def before it skipped Designator_Install, for the control pass.
        static void OldGroupsWithPostfix(Command __instance, Gizmo other, ref bool __result)
        {
            if (!__result) return;
            BuildableDef Placed(Gizmo g)
            {
                if (g is Designator_Dropdown d) g = d.Elements.Count > 0 ? d.Elements[0] : null;
                return (g as Designator_Place)?.PlacingDef;
            }
            BuildableDef a = Placed(__instance), b = Placed(other);
            if (a != null && b != null && a != b) __result = false;
        }

        static long pressedAt;

        static IEnumerable<int> DoubleClick(string label, Thing thing, int gapMs, string click = null)
        {
            Find.Selector.ClearSelection();
            Find.WindowStack.TryRemove(typeof(LudeonTK.EditWindow_Log), false);
            Find.CameraDriver.SetRootPosAndSize(thing.DrawPos, 24f);
            Stopwatch settle = Stopwatch.StartNew();
            while (settle.ElapsedMilliseconds < 1000) yield return 1; // past the double click time, so the click count starts over
            UnityEngine.Vector3 s = Find.Camera.WorldToScreenPoint(thing.DrawPos);
            UnityEngine.Vector2 screen = new UnityEngine.Vector2(s.x, s.y);
            MoveMouse(screen);
            foreach (int f in Frames(3)) yield return f;
            IntVec3 under = UI.MouseCell();
            gridErrors.Clear();
            pressedAt = 0;
            injected.Enqueue(() => pressedAt = Stopwatch.GetTimestamp());
            MouseButton(screen, true);
            yield return 1;
            yield return 1;
            MouseButton(screen, false);
            yield return 1;
            yield return 1;
            int afterFirst = Find.Selector.NumSelected;
            while (pressedAt == 0 || (Stopwatch.GetTimestamp() - pressedAt) * 1000 / Stopwatch.Frequency < gapMs) yield return 1;
            long gap = (Stopwatch.GetTimestamp() - pressedAt) * 1000 / Stopwatch.Frequency;
            MouseButton(screen, true);
            yield return 1;
            yield return 1;
            MouseButton(screen, false);
            foreach (int f in Frames(4)) yield return f;
            commandsDrawn.Clear();
            foreach (int f in Frames(4)) yield return f;
            List<object> selected = Find.Selector.SelectedObjects;
            Log.Message($"[PROBE] select {label}: {thing.def.defName} at {thing.Position}, mouse cell {under}, press gap {gap} ms, after first click {afterFirst}, after second {selected.Count}, defs {string.Join("/", selected.OfType<Thing>().Select(t => t.def.defName).Distinct().Take(4))}, grid errors {gridErrors.Count}{(gridErrors.Count > 0 ? " (" + gridErrors[0] + ")" : "")}, commands drawn {commandsDrawn.Count}: {string.Join(", ", commandsDrawn.Keys)}");

            if (click == null) yield break;
            foreach (string wanted in click.Split('|'))
            {
                string target = commandsDrawn.Keys.FirstOrDefault(k => k.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0);
                if (target == null) { Log.Message($"[PROBE] select {label}: no command matching '{wanted}' to click"); continue; }
                UnityEngine.Vector2 ui = commandsDrawn[target] + new UnityEngine.Vector2(37f, 37f);
                UnityEngine.Vector2 button = new UnityEngine.Vector2(ui.x * Prefs.UIScale, UnityEngine.Screen.height - ui.y * Prefs.UIScale);
                windowsAdded.Clear();
                gridErrors.Clear();
                MoveMouse(button);
                foreach (int f in Frames(3)) yield return f;
                MouseButton(button, true);
                yield return 1;
                yield return 1;
                MouseButton(button, false);
                foreach (int f in Frames(4)) yield return f;
                commandsDrawn.Clear();
                foreach (int f in Frames(4)) yield return f;
                Log.Message($"[PROBE] select {label}: clicked '{target}' ({commandOwners[target]}): designator {Find.DesignatorManager.SelectedDesignator?.GetType().Name}, windows opened {string.Join(",", windowsAdded)}, selected {Find.Selector.NumSelected}, grid errors {gridErrors.Count}, commands drawn {commandsDrawn.Count}: {string.Join(", ", commandsDrawn.Keys)}");
                Find.DesignatorManager.Deselect();
                foreach (Window w in Find.WindowStack.Windows.ToList()) w.Close(false);
                foreach (int f in Frames(3)) yield return f;
            }
        }

        static string PatchOwners(MethodBase m)
        {
            Patches p = Harmony.GetPatchInfo(m);
            return p == null ? "none" : string.Join("; ", p.Prefixes.Select(x => "pre " + x.owner).Concat(p.Postfixes.Select(x => "post " + x.owner)).Concat(p.Transpilers.Select(x => "trans " + x.owner)).Concat(p.Finalizers.Select(x => "fin " + x.owner)));
        }

        static IEnumerable<int> SelectPass(string pass, Map map, List<(string label, Thing thing, string click)> cases)
        {
            foreach ((string label, Thing thing, string click) in cases)
            {
                foreach (int f in DoubleClick($"{pass} {label}", thing, 150, click)) yield return f;
                foreach (int f in DoubleClick($"{pass} {label} slow", thing, 400)) yield return f;
            }
            Find.Selector.ClearSelection();

            PawnKindDef chicken = PawnKindDef.Named("Chicken");
            IntVec3 cell = map.mapPawns.FreeColonistsSpawned.First().Position;
            Pawn parent = PawnGenerator.GeneratePawn(chicken, Faction.OfPlayer);
            GenSpawn.Spawn(parent, cell, map);
            parent.Destroy();
            if (Find.WorldPawns.Contains(parent)) Find.WorldPawns.RemovePawn(parent);
            if (!parent.Discarded) parent.Discard();
            Thing egg = ThingMaker.MakeThing(ThingDef.Named("EggChickenFertilized"));
            CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
            hatcher.hatcheeParent = parent;
            hatcher.hatcheeFaction = Faction.OfPlayer;
            GenSpawn.Spawn(egg, cell, map);
            hatcher.Hatch();
            List<Pawn> hatched = map.mapPawns.AllPawnsSpawned.Where(p => p.kindDef == chicken && p.thingIDNumber > parent.thingIDNumber).ToList();
            Log.Message($"[PROBE] {pass} hatched {hatched.Count} from an egg of a parent with Discarded={parent.Discarded}, relations to it: {hatched.Sum(p => p.relations.DirectRelations.Count(r => r.otherPawn == parent))}");
            foreach (Pawn p in hatched) p.relations.ClearAllRelations(); // so the control's relation does not reach the temporary save
            foreach (Pawn p in hatched) p.Destroy();

            QuestPart_DropPods pods = new QuestPart_DropPods { mapParent = map.Parent };
            MethodInfo randomSpot = AccessTools.Method(typeof(QuestPart_DropPods), "GetRandomDropSpot");
            int spots = 2000, inWater = 0, noLanding = 0;
            for (int i = 0; i < spots; i++)
            {
                IntVec3 spot = (IntVec3)randomSpot.Invoke(pods, null);
                if (spot.GetTerrain(map).IsWater) inWater++;
                if (!DropCellFinder.TryFindDropSpotNear(spot, map, out IntVec3 _, false, true)) noLanding++;
                if (i % 100 == 99) yield return 1;
            }
            Log.Message($"[PROBE] {pass} quest drop spots: {spots} picks, {inWater} in water, {noLanding} with no landing cell near");
        }

        static IEnumerable<int> SelectTest()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Map map = Find.AnyPlayerHomeMap;
            Current.Game.CurrentMap = map;
            Harmony h = new Harmony("local.lp17.probe");
            h.Patch(AccessTools.Method(inputState, "BeginFrame"), postfix: new HarmonyMethod(typeof(Probe), nameof(InjectNow)));
            h.Patch(AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid)), finalizer: new HarmonyMethod(typeof(Probe), nameof(GridFinalizer)));
            h.Patch(AccessTools.Method(typeof(Command), nameof(Command.GizmoOnGUI)), prefix: new HarmonyMethod(typeof(Probe), nameof(CommandDrawn)));
            h.Patch(AccessTools.Method(typeof(WindowStack), nameof(WindowStack.Add)), prefix: new HarmonyMethod(typeof(Probe), nameof(WindowAdded)));

            foreach (string name in new[] { "HandleMapClicks", "SelectorOnGUI", "SelectUnderMouse", "SelectAllMatchingObjectUnderMouseOnScreen", "SelectInsideDragBox", "Select", "ClearSelection" })
                Log.Message($"[PROBE] patches Selector.{name}: {PatchOwners(AccessTools.Method(typeof(Selector), name))}");
            Log.Message($"[PROBE] patches MultiSelectableThingsInScreenRectDistinct: {PatchOwners(AccessTools.Method(typeof(ThingSelectionUtility), nameof(ThingSelectionUtility.MultiSelectableThingsInScreenRectDistinct)))}");
            Log.Message($"[PROBE] patches SelectableByMapClick: {PatchOwners(AccessTools.Method(typeof(ThingSelectionUtility), nameof(ThingSelectionUtility.SelectableByMapClick)))}");
            Log.Message($"[PROBE] patches DrawGizmoGrid: {PatchOwners(AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid)))}");
            Log.Message($"[PROBE] patches Command.GroupsWith: {PatchOwners(AccessTools.Method(typeof(Command), nameof(Command.GroupsWith)))}");

            Dictionary<ushort, List<string>> byHash = new Dictionary<ushort, List<string>>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!byHash.TryGetValue(d.shortHash, out List<string> names)) byHash[d.shortHash] = names = new List<string>();
                names.Add(d.defName);
            }
            List<List<string>> clashes = byHash.Values.Where(n => n.Count > 1).ToList();
            Log.Message($"[PROBE] short hash clashes {clashes.Count}: {string.Join("; ", clashes.Take(10).Select(n => string.Join(" = ", n)))}; RP_ defs {DefDatabase<ThingDef>.AllDefs.Count(d => d.defName.StartsWith("RP_Diffusecyst"))}, frames {DefDatabase<ThingDef>.AllDefs.Count(d => d.defName.EndsWith("_ReplaceStuff"))}");

            IEnumerable<Thing> Grouped(Func<Thing, bool> where) => map.listerThings.AllThings.Where(t => t.Faction == Faction.OfPlayer && !t.Position.Fogged(map) && where(t)).GroupBy(t => t.def).Where(g => g.Count() >= 3).OrderByDescending(g => g.Count()).Select(g => g.First());
            ThingDef chair = ThingDef.Named("DiningChair");
            IntVec3 center = map.mapPawns.FreeColonistsSpawned.First().Position;
            List<IntVec3> free = GenRadial.RadialCellsAround(center, 25f, true).Where(c => c.InBounds(map) && !c.Fogged(map) && !c.Roofed(map) && c.GetThingList(map).Count == 0 && GenConstruct.CanPlaceBlueprintAt(chair, c, Rot4.North, map, false, null, null, GenStuff.DefaultStuffFor(chair)).Accepted && !c.GetTerrain(map).IsWater).Take(3).ToList();
            foreach (var g in DefDatabase<ThingDef>.AllDefs.Where(d => d.neverMultiSelect).GroupBy(d => d.category)) Log.Message($"[PROBE] neverMultiSelect {g.Key}: {g.Count()}, sample {string.Join(",", g.Take(6).Select(d => d.defName))}");
            Log.Message("[PROBE] neverMultiSelect furniture: " + string.Join(",", DefDatabase<ThingDef>.AllDefs.Where(d => d.neverMultiSelect && d.category == ThingCategory.Building && d.Minifiable).Take(30).Select(d => d.defName)));
            List<Thing> blueprints = free.Select(c => (Thing)GenConstruct.PlaceBlueprintForBuild(chair, c, map, Rot4.North, Faction.OfPlayer, GenStuff.DefaultStuffFor(chair))).ToList();
            Log.Message($"[PROBE] blueprints placed {blueprints.Count}, chair can be styled {ThingStyleHelper.CanBeStyled(chair)}, classic ideo {Find.IdeoManager.classicMode}");

            List<(string, Thing, string)> cases = new List<(string, Thing, string)>
            {
                ("furniture", Grouped(t => t is Building && t.def.Minifiable).FirstOrDefault(t => t.def.category == ThingCategory.Building), "Copy|Deconstruct"),
                ("styleable", Grouped(t => t is Building && t.def.Minifiable && ThingStyleHelper.CanBeStyled(t.def)).FirstOrDefault(t => t.def.category == ThingCategory.Building), "style"),
                ("wall", Grouped(t => t is Building && !t.def.Minifiable && t.def.fillPercent >= 1f).FirstOrDefault(), "Select similar|style"),
                ("blueprint", blueprints.FirstOrDefault(), "style"),
                ("animal", map.mapPawns.SpawnedColonyAnimals.GroupBy(p => p.def).Where(g => g.Count() >= 2).Select(g => (Thing)g.First()).FirstOrDefault(), null),
                ("colonist", map.mapPawns.FreeColonistsSpawned.FirstOrDefault(), null),
            };
            cases.RemoveAll(c => c.Item2 == null);

            foreach (int f in SelectPass("fixed", map, cases)) yield return f;

            Harmony hot = new Harmony(Lp.Id);
            hot.Unpatch(AccessTools.Method(typeof(Command), nameof(Command.GroupsWith)), HarmonyPatchType.All, Lp.Id);
            h.Patch(AccessTools.Method(typeof(Command), nameof(Command.GroupsWith)), postfix: new HarmonyMethod(typeof(Probe), nameof(OldGroupsWithPostfix)));
            hot.Unpatch(AccessTools.Method(inputState, "OnMouseButton"), HarmonyPatchType.All, Lp.Id);
            hot.Unpatch(AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation)), HarmonyPatchType.All, Lp.Id);
            hot.Unpatch(AccessTools.Method(typeof(QuestPart_DropPods), "GetRandomDropSpot"), HarmonyPatchType.All, Lp.Id);
            foreach (int f in SelectPass("control", map, cases)) yield return f;

            foreach (Thing b in blueprints) if (b.Spawned) b.Destroy();
            Mark("temporary save");
            GameDataSaveLoader.SaveGame("LP17ProbeTemp");
            File.Delete(GenFilePaths.FilePathForSavedGame("LP17ProbeTemp"));
            Mark("saved");
            foreach (int f in Frames(10)) yield return f;
        }

        // ---- new game page fixes: body texture, classic ideo ritual names, history before game start ----

        static void ClassicIdeos()
        {
            IdeoGenerationParms parms = new IdeoGenerationParms(Find.FactionManager.OfPlayer.def);
            List<CultureDef> cultures = DefDatabase<CultureDef>.AllDefs.Where(c => Find.FactionManager.OfPlayer.def.allowedCultures.Contains(c)).ToList();
            if (cultures.Count == 0) cultures = DefDatabase<CultureDef>.AllDefsListForReading;
            for (int i = 0; i < 30; i++) IdeoGenerator.GenerateClassicIdeo(cultures[i % cultures.Count], parms, noExpansionIdeo: false);
        }

        static IEnumerable<int> NewGameTest()
        {
            const string path = "Things/Pawn/Humanlike/Bodies/Naked_Female_south";
            UnityEngine.Texture2D found = ContentFinder<UnityEngine.Texture2D>.Get(path), vanilla = UnityEngine.Resources.Load<UnityEngine.Texture2D>(GenFilePaths.ContentPath<UnityEngine.Texture2D>() + path);
            ModContentPack za = LoadedModManager.RunningMods.FirstOrDefault(m => m.PackageIdPlayerFacing.ToLowerInvariant() == "zx.costumes");
            bool zaHolds = za != null && za.GetContentHolder<UnityEngine.Texture2D>().contentList.Keys.Any(k => k.Contains("Naked_"));
            int under = za == null ? -1 : za.GetContentHolder<UnityEngine.Texture2D>().GetAllUnderPath("Things/Pawn/Humanlike/Bodies").Count();
            Log.Message($"[PROBE] body: za active={za != null}, za holds body={zaHolds}, listed under path={under}, found={found != null}, same as game's={found == vanilla}");
            yield return 1;

            Type tracker = AccessTools.TypeByName("ModernNotifications.OccasionsTracker");
            MethodInfo record = AccessTools.Method(tracker, "RecordHistory");
            System.Collections.IList history = (System.Collections.IList)AccessTools.Field(tracker, "_history").GetValue(AccessTools.Property(tracker, "Instance").GetValue(null));
            object[] entry = { Enum.ToObject(record.GetParameters()[0].ParameterType, 0), "probe", "", "" };
            TickManager ticks = Find.TickManager;
            int start = ticks.gameStartAbsTick;

            Log.Message("[PROBE] fixed start");
            ClassicIdeos();
            int before = history.Count;
            ticks.gameStartAbsTick = 0;
            record.Invoke(null, entry);
            ticks.gameStartAbsTick = start;
            Log.Message($"[PROBE] fixed end: history added={history.Count - before} (want 0)");
            yield return 1;

            // Same steps with the fixes off: these errors must appear, or the test above proves nothing.
            Harmony h = new Harmony("local.lp17.hotfix");
            h.Unpatch(AccessTools.Method(typeof(Precept_Ritual), nameof(Precept_Ritual.GenerateNameRaw)), HarmonyPatchType.Prefix, "local.lp17.hotfix");
            h.Unpatch(record, HarmonyPatchType.Prefix, "local.lp17.hotfix");
            Log.Message("[PROBE] control start");
            ClassicIdeos();
            before = history.Count;
            ticks.gameStartAbsTick = 0;
            record.Invoke(null, entry);
            ticks.gameStartAbsTick = start;
            Log.Message($"[PROBE] control end: history added={history.Count - before} (want 1)");
        }

        // ---- mod conflict fixes: each check runs with the fixes on, then again with them unpatched, where the opposite must hold ----

        static int conflictChecks, conflictSkips;

        static void CheckC(bool ok, string what) { conflictChecks++; Expect(ok, what); }

        static void SkipC(string name, string why) { conflictSkips++; Log.Message($"[PROBE] skip {name}: {why}"); }

        static IEnumerable<MethodBase> ConflictTargets()
        {
            yield return AccessTools.Method(typeof(JobDriver_Ingest), "PrepareToIngestToils_Dispenser");
            yield return AccessTools.Method(typeof(ITab_Pawn_Gear), "DrawThingRow");
            yield return AccessTools.Method("VanillaQuestsExpandedTheGenerator.VanillaQuestsExpandedTheGenerator_PowerNet_PowerNetTick_Patch:PowerBatteries");
            yield return AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot");
            yield return AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawAimPie));
        }

        static IEnumerable<int> ConflictsTest()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Map map = Find.AnyPlayerHomeMap;
            Current.Game.CurrentMap = map;
            foreach (int f in ConflictsPass("fixed", map)) yield return f;

            Harmony hot = new Harmony(Lp.Id);
            foreach (MethodBase m in ConflictTargets().Where(m => m != null).ToList()) hot.Unpatch(m, HarmonyPatchType.All, Lp.Id);
            foreach (int f in ConflictsPass("control", map)) yield return f;
            Log.Message($"[PROBE] conflicts failures: {autoFails}, checks {conflictChecks}, skips {conflictSkips}");
        }

        static int DispenserToils(MethodInfo method, Pawn pawn)
        {
            try { return ((IEnumerable<Toil>)method.Invoke(new JobDriver_Ingest { pawn = pawn }, null)).ToList().Count; }
            catch (Exception e) { Log.Message($"[PROBE] dispenser toils threw for {pawn}: {First((e.InnerException ?? e).Message)}"); return -1; }
        }

        static IEnumerable<PatchInfoRow> PatchRows(MethodBase method, bool prefixes)
        {
            Patches info = Harmony.GetPatchInfo(method);
            return info == null ? new List<PatchInfoRow>() : (prefixes ? info.Prefixes : info.Transpilers).Select(p => new PatchInfoRow { owner = p.owner, priority = p.priority });
        }

        struct PatchInfoRow { public string owner; public int priority; }

        static Thing SpawnAtFreeCell(Map map, ThingDef d)
        {
            if (!CellFinderLoose.TryGetRandomCellWith(c => c.Standable(map) && c.GetFirstBuilding(map) == null && GenAdj.OccupiedRect(c, Rot4.North, d.size).InBounds(map), map, 200, out IntVec3 cell)) return null;
            Thing t = ThingMaker.MakeThing(d, d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null);
            if (t.def.CanHaveFaction && d.category == ThingCategory.Building) t.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(t, cell, map, WipeMode.Vanish);
        }

        static bool RtShieldActive(Map map, Type compType)
        {
            MethodInfo coordinator = null;
            try { coordinator = compType.Assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)).FirstOrDefault(m => m.Name == "GetShieldCoordinator"); }
            catch (ReflectionTypeLoadException) { }
            object coord = coordinator?.Invoke(null, new object[] { map });
            MethodInfo active = coord == null ? null : AccessTools.Method(coord.GetType(), "HasActiveShield");
            if (active == null) throw new InvalidOperationException("RT's shield coordinator not found");
            return (bool)active.Invoke(coord, null);
        }

        static IEnumerable<int> ConflictsPass(string pass, Map map)
        {
            bool fix = pass == "fixed";
            string tag = fix ? "" : "control ";
            Log.Message($"[PROBE] conflicts {pass} start");
            Pawn colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();

            // 1. dispenser
            Type commonSense = AccessTools.TypeByName("CommonSense.Settings");
            FieldInfo advIngest = commonSense == null ? null : AccessTools.Field(commonSense, "adv_cleaning_ingest");
            MethodInfo dispenser = AccessTools.Method(typeof(JobDriver_Ingest), "PrepareToIngestToils_Dispenser");
            Pawn animal = map.mapPawns.SpawnedColonyAnimals.FirstOrDefault();
            if (advIngest == null || dispenser == null || animal == null || colonist == null) SkipC("dispenser", "Common Sense setting, the dispenser method, an animal or a colonist is missing");
            else
            {
                bool old = (bool)advIngest.GetValue(null);
                advIngest.SetValue(null, true);
                try
                {
                    int animalToils = DispenserToils(dispenser, animal), colonistToils = DispenserToils(dispenser, colonist);
                    CheckC(fix ? animalToils == 3 : animalToils != 3, $"{tag}dispenser: animal gets {animalToils} toils{(fix ? "" : " (not 3)")}");
                    CheckC(colonistToils > 3, $"{tag}dispenser: colonist gets {colonistToils} toils (more than 3)");
                }
                finally { advIngest.SetValue(null, old); }
            }
            yield return 1;

            // 2. gear row
            ThingDef lantern = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.thingClass != null && d.thingClass.FullName == "LanternMod.Apparel_Lantern");
            MethodInfo gearRow = AccessTools.Method(typeof(ITab_Pawn_Gear), "DrawThingRow");
            if (lantern == null || colonist == null || gearRow == null) SkipC("gear row", "Belt Lantern def, a colonist or the gear row method is missing");
            else
            {
                Apparel worn = (Apparel)ThingMaker.MakeThing(lantern, lantern.MadeFromStuff ? GenStuff.DefaultStuffFor(lantern) : null);
                try
                {
                    colonist.apparel.Wear(worn, false);
                    Apparel other = colonist.apparel.WornApparel.FirstOrDefault(a => a != worn);
                    if (!colonist.apparel.WornApparel.Contains(worn)) SkipC("gear row", "colonist could not wear the lantern");
                    else if (fix)
                    {
                        CheckC(!GearRowHiddenFirst.Prefix(worn), "gear row: the hiding prefix hides the lantern row");
                        if (other == null) SkipC("gear row shows other", "colonist wears nothing else");
                        else CheckC(GearRowHiddenFirst.Prefix(other), "gear row: another worn apparel row still shows");
                        List<PatchInfoRow> rows = PatchRows(gearRow, true).OrderByDescending(r => r.priority).ToList();
                        CheckC(rows.Count > 0 && rows[0].owner == Lp.Id, $"gear row: this mod's prefix runs first (first owner {(rows.Count > 0 ? rows[0].owner : "none")})");
                    }
                    else CheckC(!PatchRows(gearRow, true).Any(r => r.owner == Lp.Id), "control gear row: no prefix of this mod on DrawThingRow");
                }
                finally
                {
                    if (colonist.apparel.WornApparel.Contains(worn)) colonist.apparel.Remove(worn);
                    if (!worn.Destroyed) worn.Destroy();
                }
            }
            yield return 1;

            // 3. flare shield
            MethodInfo powerBatteries = AccessTools.Method("VanillaQuestsExpandedTheGenerator.VanillaQuestsExpandedTheGenerator_PowerNet_PowerNetTick_Patch:PowerBatteries");
            PowerNet net = map.powerNetManager.AllNetsListForReading.FirstOrDefault();
            ThingDef shieldDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.comps != null && d.comps.Any(c => c.compClass != null && c.compClass.Name == "CompRTSolarFlareShield"));
            if (powerBatteries == null || net == null || shieldDef == null) SkipC("flare shield", "VQE The Generator battery method, a power net or the RT shield def is missing");
            else
            {
                if (fix) CheckC(!FlareShieldGenetron.Batteries(net), "flare shield: no flare, battery path off");
                GameCondition flare = GameConditionMaker.MakeCondition(DefDatabase<GameConditionDef>.GetNamed("SolarFlare"), 60000);
                map.gameConditionManager.RegisterCondition(flare);
                Thing shield = null;
                try
                {
                    CheckC(map.gameConditionManager.ElectricityDisabled(map), $"{tag}flare shield: a solar flare disables electricity");
                    Type compType = shieldDef.comps.First(c => c.compClass != null && c.compClass.Name == "CompRTSolarFlareShield").compClass;
                    bool already = false;
                    try { already = RtShieldActive(map, compType); }
                    catch (Exception e) { SkipC("flare shield, no shield", First(e.Message)); already = true; }
                    if (already) SkipC("flare shield, no shield", "a shield is already active or cannot be read");
                    else if (fix) CheckC(FlareShieldGenetron.Batteries(net), "flare shield: flare, no shield, battery path on");

                    shield = SpawnAtFreeCell(map, shieldDef);
                    if (shield == null) SkipC("flare shield, shield active", "no free cell for the shield");
                    else
                    {
                        CompPowerTrader power = shield.TryGetComp<CompPowerTrader>();
                        if (power != null) power.PowerOn = true;
                        foreach (int x in Ticks(2)) yield return x;
                        if (power != null) power.PowerOn = true; // a net tick may have switched it off
                        bool active = false;
                        try { active = RtShieldActive(map, compType); }
                        catch (Exception) { }
                        if (!active) SkipC("flare shield, shield active", "RT does not count the spawned shield as active");
                        else if (fix) CheckC(!FlareShieldGenetron.Batteries(net), "flare shield: flare under a shield, battery path off");
                    }
                    // The control pass does not call Batteries: it is this mod's own method and answers the same either way.
                    bool hooked = PatchRows(powerBatteries, true).Any(r => r.owner == Lp.Id);
                    CheckC(fix ? hooked : !hooked, $"{tag}flare shield: this mod's prefix on PowerBatteries is {(fix ? "present" : "gone")}");
                }
                finally
                {
                    if (shield != null && !shield.Destroyed) shield.Destroy();
                    flare.End();
                }
            }
            yield return 1;

            // 4. cast source
            Type barrels = AccessTools.TypeByName("VFED.Building_TurretGunBarrels"), gravship = AccessTools.TypeByName("VanillaGravshipExpanded.Building_GravshipTurret");
            MethodInfo gravshipSource = gravship == null ? null : AccessTools.Method(gravship, "GetCastSource");
            MethodInfo tryCast = AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot"), aimPie = AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawAimPie));
            if (barrels == null || gravshipSource == null) SkipC("cast source", "VFE Deserters or Vanilla Gravship Expanded is missing");
            else
            {
                bool patched = PatchRows(aimPie, false).Any(r => r.owner == Lp.Id) && PatchRows(tryCast, false).Any(r => r.owner == Lp.Id);
                CheckC(fix ? patched : !PatchRows(aimPie, false).Any(r => r.owner == Lp.Id) && !PatchRows(tryCast, false).Any(r => r.owner == Lp.Id), $"{tag}cast source: this mod's transpiler on DrawAimPie and TryCastShot is {(fix ? "present" : "gone")}");
                if (fix)
                {
                    bool checkedOne = false;
                    foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs.Where(d => d.category == ThingCategory.Building && d.thingClass != null && gravship.IsAssignableFrom(d.thingClass)).ToList())
                    {
                        Thing turret = null;
                        try
                        {
                            turret = SpawnAtFreeCell(map, d);
                            if (turret == null) continue;
                            UnityEngine.Vector3 theirs = (UnityEngine.Vector3)gravshipSource.Invoke(null, new object[] { turret }), ours = CastSourceChain.Either(turret), draw = turret.DrawPos;
                            bool differs = (theirs - draw).sqrMagnitude > 1e-6f;
                            if (!differs) continue;
                            CheckC((ours - theirs).sqrMagnitude < 1e-6f, $"cast source: {d.defName} chain gives {ours}, gravship helper {theirs}, DrawPos {draw}, cast source differs from DrawPos: {differs}");
                            checkedOne = true;
                            break;
                        }
                        finally { if (turret != null && !turret.Destroyed) turret.Destroy(); }
                    }
                    if (!checkedOne) SkipC("cast source, chain value", "no gravship turret def has a cast source different from its DrawPos");
                }
            }
            yield return 1;
        }

        static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Message($"[PROBE-EX] {what} | {e.GetType().Name} | {First(e.Message)} | {string.Join(" <- ", (e.StackTrace ?? "").Split('\n').Take(4).Select(s => s.Trim().Replace("at ", "")))}"); }
        }

        static void Mark(string s) => Log.Message("[PROBE-STEP] " + s);

        static IEnumerable<int> Ticks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Guard("tick", () => Find.TickManager.DoSingleTick());
                yield return 0;
            }
        }

        // ---- architect menu and timings ----

        static object Extension(Def d, string typeName, string field)
        {
            DefModExtension x = d.modExtensions?.FirstOrDefault(e => e.GetType().Name == typeName);
            return x == null ? null : AccessTools.Field(x.GetType(), field)?.GetValue(x);
        }

        static IEnumerable<Designator> Flat(IEnumerable<Designator> list)
        {
            foreach (Designator d in list)
            {
                yield return d;
                if (d is Designator_Dropdown dd) foreach (Designator e in dd.Elements) yield return e;
            }
        }

        static void ArchitectScan(Map map)
        {
            var resolved = new Dictionary<BuildableDef, string>();
            var allowed = new HashSet<BuildableDef>();
            var visible = new HashSet<BuildableDef>();
            foreach (DesignationCategoryDef c in DefDatabase<DesignationCategoryDef>.AllDefs)
            {
                foreach (Designator d in Flat(c.AllResolvedDesignators))
                    if (d is Designator_Place p && p.PlacingDef != null)
                    {
                        resolved[p.PlacingDef] = c.defName;
                        if (d.Visible) visible.Add(p.PlacingDef);
                    }
                foreach (Designator d in Flat(c.ResolvedAllowedDesignators))
                    if (d is Designator_Place p && p.PlacingDef != null) allowed.Add(p.PlacingDef);
            }
            Type hiddenType = AccessTools.TypeByName("BetterArchitect.VefHiddenDesignatorCache");
            hiddenType?.GetMethod("Initialize")?.Invoke(null, null);
            MethodInfo isHidden = hiddenType == null ? null : AccessTools.Method(hiddenType, "IsHidden", new[] { typeof(BuildableDef) });
            Table("architectcats", new[] { "defName", "label", "mod", "parent", "order", "resolved", "allowed", "visible" },
                DefDatabase<DesignationCategoryDef>.AllDefs.Select(c => new object[] { c.defName, c.label, c.modContentPack?.PackageIdPlayerFacing, (Extension(c, "NestedCategoryExtension", "parentCategory") as Def)?.defName, c.order, c.AllResolvedDesignators.Count, c.ResolvedAllowedDesignators.Count(), c.ResolvedAllowedDesignators.Count(d => d.Visible) }));
            string Materials(BuildableDef b) => Join(b.costList?.Select(x => x.thingDef?.defName + (x.thingDef != null && map.listerThings.ThingsOfDef(x.thingDef).Any() ? "" : "!")));
            Table("architect", new[] { "defName", "label", "mod", "kind", "category", "dropdown", "cost", "stuffCount", "stuffCats", "research", "researchDone", "minTech", "maxTech", "prereqs", "resolved", "allowed", "visible", "vefHidden" },
                DefDatabase<TerrainDef>.AllDefs.Cast<BuildableDef>().Concat(DefDatabase<ThingDef>.AllDefs.Where(t => t.category == ThingCategory.Building)).Where(b => b.designationCategory != null || !b.costList.NullOrEmpty() || b.costStuffCount > 0).Select(b => new object[]
                {
                    b.defName, b.label, b.modContentPack?.PackageIdPlayerFacing, b is TerrainDef ? "terrain" : "thing", b.designationCategory?.defName, b.designatorDropdown?.defName,
                    Materials(b), b.costStuffCount, Join(b.stuffCategories?.Select(s => s.defName)), Join(b.researchPrerequisites?.Select(r => r.defName)), b.IsResearchFinished, b.minTechLevelToBuild, b.maxTechLevelToBuild,
                    Join(b.buildingPrerequisites?.Select(p => p.defName)), resolved.TryGetValue(b, out string cat) ? cat : "", allowed.Contains(b), visible.Contains(b), isHidden != null && (bool)isHidden.Invoke(null, new object[] { b })
                }));
        }

        sealed class Timer { public string owner, patch, target; public long calls, stamps; }

        static readonly Dictionary<MethodBase, Timer> timers = new Dictionary<MethodBase, Timer>();

        static void TimeIn(out long __state) => __state = Stopwatch.GetTimestamp();

        static void TimeOut(long __state, MethodBase __originalMethod)
        {
            if (!timers.TryGetValue(__originalMethod, out Timer t)) return;
            System.Threading.Interlocked.Increment(ref t.calls);
            System.Threading.Interlocked.Add(ref t.stamps, Stopwatch.GetTimestamp() - __state);
        }

        static void Time(Harmony h, MethodInfo m, string owner, string target)
        {
            if (m == null || m.IsAbstract || m.IsGenericMethodDefinition || m.ContainsGenericParameters || timers.ContainsKey(m)) return;
            timers[m] = new Timer { owner = owner, patch = m.DeclaringType?.FullName + "." + m.Name, target = target };
            Guard("time " + m.Name, () => h.Patch(m, prefix: new HarmonyMethod(typeof(Probe), nameof(TimeIn)), finalizer: null, postfix: new HarmonyMethod(typeof(Probe), nameof(TimeOut))));
        }

        static void TimeAll(Map map)
        {
            Harmony h = new Harmony("local.lp17.probe.timer");
            string Name(MethodBase b) => b.DeclaringType?.Name + "." + b.Name;
            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToList())
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                    if (!p.owner.StartsWith("local.lp17.probe")) Time(h, p.PatchMethod, p.owner, Name(target));
            }
            string[] names = { "GameComponentTick", "GameComponentUpdate", "GameComponentOnGUI", "MapComponentTick", "MapComponentUpdate", "MapComponentOnGUI", "WorldComponentTick", "WorldComponentUpdate" };
            IEnumerable<object> comps = Current.Game.components.Cast<object>().Concat(map.components).Concat(Find.World.components);
            foreach (object c in comps)
                foreach (string n in names)
                    Time(h, c.GetType().GetMethod(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), c.GetType().Assembly.GetName().Name, c.GetType().Name + "." + n);
            foreach ((Type t, string n) in new[] { (typeof(TickManager), "DoSingleTick"), (typeof(Map), "MapPreTick"), (typeof(Map), "MapPostTick"), (typeof(Map), "MapUpdate"), (typeof(TickList), "Tick"), (typeof(RimWorld.Planet.World), "WorldTick"), (typeof(Pawn), "Tick"), (typeof(Root_Play), "Update"), (typeof(Root), "OnGUI"), (typeof(RimWorld.UIRoot_Play), "UIRootOnGUI") })
                Time(h, AccessTools.Method(t, n), "(top)", t.Name + "." + n);
            Log.Message("[PROBE] timing " + timers.Count + " methods");
        }

        static string DesName(Designator d) => d is Designator_Dropdown dd ? "DD[" + Join(dd.Elements.Select(DesName)) + "]" : d is Designator_Place p ? p.PlacingDef?.defName : d.GetType().Name;

        static int floorLogs;

        static void FloorsPost(List<Designator> allFloorDesignators, List<Designator> __result)
        {
            if (floorLogs++ > 1) return;
            Log.Message("[PROBE] floors tab got " + allFloorDesignators.Count + ": " + Join(allFloorDesignators.Select(DesName)));
            Log.Message("[PROBE] floors tab shows " + __result.Count + ": " + Join(__result.Select(DesName)));
            if (floorLogs > 1) return;
            Guard("floors trace", () =>
            {
                object cached = AccessTools.Field(AccessTools.TypeByName("BetterArchitect.ArchitectCategoryTab_DesignationTabOnGUI_Patch"), "cachedRawDesignatorData").GetValue(null);
                List<Designator> unique = new List<Designator>();
                foreach (object data in (System.Collections.IEnumerable)cached)
                {
                    DesignationCategoryDef def = (DesignationCategoryDef)AccessTools.Field(data.GetType(), "def").GetValue(data);
                    List<Designator> all = (List<Designator>)AccessTools.Field(data.GetType(), "allDesignators").GetValue(data);
                    Log.Message("[PROBE] floor category " + def.defName + " has " + all.Count);
                    foreach (Designator d in all)
                    {
                        Designator u = unique.FirstOrDefault(x => x.GroupsWith(d));
                        if (u != null) Log.Message($"[PROBE] merged {DesName(d)} '{d.Label}' icon {d.icon?.name} into {DesName(u)} '{u.Label}' icon {u.icon?.name} hot {u.hotKey?.defName} key {u.groupKey}/{u.groupKeyIgnoreContent}");
                        else unique.Add(d);
                    }
                }
                Log.Message("[PROBE] unique " + unique.Count + ": " + Join(unique.OfType<Designator_Dropdown>().Select(d => "'" + d.Label + "' " + d.Elements.Count)));
            });
        }

        static void OpenFloors()
        {
            Type t = AccessTools.TypeByName("BetterArchitect.ArchitectCategoryTab_DesignationTabOnGUI_Patch");
            new Harmony("local.lp17.probe").Patch(AccessTools.Method(t, "DrawMaterialListForFloors"), postfix: new HarmonyMethod(typeof(Probe), nameof(FloorsPost)));
            MainButtonDef arch = DefDatabase<MainButtonDef>.GetNamed("Architect");
            Find.MainTabsRoot.SetCurrentTab(arch, false);
            MainTabWindow_Architect win = (MainTabWindow_Architect)arch.TabWindow;
            win.selectedDesPanel = ((List<ArchitectCategoryTab>)AccessTools.Field(typeof(MainTabWindow_Architect), "desPanelsCached").GetValue(win)).First(p => p.def == DesignationCategoryDefOf.Floors);
        }

        // Better Architect shows a tab and its nested categories together, keeping one designator per label and icon.
        static void ArchitectCollisions()
        {
            int found = 0;
            foreach (IGrouping<DesignationCategoryDef, DesignationCategoryDef> tab in DefDatabase<DesignationCategoryDef>.AllDefs.GroupBy(c => (Extension(c, "NestedCategoryExtension", "parentCategory") as DesignationCategoryDef) ?? c))
            {
                List<Designator> all = tab.SelectMany(c => c.ResolvedAllowedDesignators).Where(d => d.Visible && !(d is Designator_Dropdown dd && dd.Elements.Count == 0)).ToList();
                foreach (IGrouping<string, Designator> same in all.Where(d => d is Designator_Place || d is Designator_Dropdown).GroupBy(d => d.Label + "|" + d.icon?.name))
                {
                    List<string> names = same.Select(DesName).Distinct().ToList();
                    if (names.Count < 2) continue;
                    found++;
                    Log.Message($"[PROBE] same label and icon in tab {tab.Key.defName}: '{same.Key}': {Join(names)}");
                }
            }
            Log.Message("[PROBE] label and icon collisions: " + found);
        }

        static IEnumerable<int> Natural(string label, TimeSpeed speed, double seconds)
        {
            Find.TickManager.CurTimeSpeed = speed;
            List<double> frames = new List<double>();
            int startTick = Find.TickManager.TicksGame, gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            TimeSpan pause = GC.GetTotalPauseDuration();
            long alloc = GC.GetTotalAllocatedBytes(false);
            Stopwatch total = Stopwatch.StartNew(), frame = Stopwatch.StartNew();
            while (total.Elapsed.TotalSeconds < seconds)
            {
                yield return 1;
                frames.Add(frame.Elapsed.TotalMilliseconds);
                frame.Restart();
            }
            frames.Sort();
            double At(double f) => frames[Math.Min(frames.Count - 1, (int)(frames.Count * f))];
            double s = total.Elapsed.TotalSeconds;
            Log.Message($"[PROBE] natural {label}: {s:F1} s, tps {(Find.TickManager.TicksGame - startTick) / s:F1}, fps {frames.Count / s:F1}, frame ms p50 {At(0.5):F1} p95 {At(0.95):F1} p99 {At(0.99):F1} max {frames[frames.Count - 1]:F1}, gc {GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2}, pause {(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F0} ms, alloc {(GC.GetTotalAllocatedBytes(false) - alloc) / (1024 * 1024) / s:F0} MB/s, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB, gc committed {GC.GetGCMemoryInfo().TotalCommittedBytes / (1024 * 1024)} MB, latency {System.Runtime.GCSettings.LatencyMode}, server {System.Runtime.GCSettings.IsServerGC}");
        }

        static IEnumerable<int> Scan()
        {
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            Mark("architect");
            Guard("architect", () => ArchitectScan(map));
            yield return 1;
            Mark("floors tab");
            Guard("floors tab", OpenFloors);
            for (int i = 0; i < 5; i++) yield return 1;
            Find.MainTabsRoot.EscapeCurrentTab(false);
            Guard("collisions", ArchitectCollisions);
            yield return 1;
            Guard("save timing", () =>
            {
                Stopwatch save = Stopwatch.StartNew();
                GameDataSaveLoader.SaveGame("LP17ProbeTemp");
                string path = GenFilePaths.FilePathForSavedGame("LP17ProbeTemp");
                Log.Message($"[PROBE] save took {save.ElapsedMilliseconds} ms, {new FileInfo(path).Length / (1024 * 1024)} MB");
                // compressed-things-by-name: the save must name its compressed defs, and the loaded map must hold the ore the old save named.
                string text = File.ReadAllText(path);
                int at = text.IndexOf("<lp17CompressedThingDefs>", StringComparison.Ordinal);
                int end = at < 0 ? -1 : text.IndexOf("</lp17CompressedThingDefs>", at, StringComparison.Ordinal);
                Log.Message(end < 0 ? "[PROBE] FAIL save has no compressed thing names" : "[PROBE] ok save names compressed things: " + string.Join(" ", text.Substring(at, end - at).Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)));
                Log.Message("[PROBE] silver ore cells on map: " + Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("MineableSilver")).Count);
                File.Delete(path);
            });
            yield return 1;
            if (Environment.GetEnvironmentVariable("LP17_PROBE_MENU_ONLY") == "1") yield break;
            Mark("settle");
            foreach (int x in Ticks(600)) yield return x;
            Mark("natural");
            foreach (int x in Natural("warm", TimeSpeed.Superfast, 45)) yield return x;
            foreach (int x in Natural("paused", TimeSpeed.Paused, 15)) yield return x;
            foreach (int x in Natural("normal", TimeSpeed.Normal, 30)) yield return x;
            foreach (int x in Natural("fast", TimeSpeed.Fast, 30)) yield return x;
            foreach (int x in Natural("superfast", TimeSpeed.Superfast, 30)) yield return x;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Mark("wrap");
            Guard("wrap", () => TimeAll(map));
            yield return 1;
            int count = int.TryParse(Environment.GetEnvironmentVariable("LP17_PROBE_TICKS"), out int parsed) ? parsed : 3600;
            Mark("timing " + count);
            foreach (Timer t in timers.Values) { t.calls = 0; t.stamps = 0; }
            long gc = GC.CollectionCount(0), frames = UnityEngine.Time.frameCount;
            Stopwatch all = Stopwatch.StartNew();
            foreach (int x in Ticks(count)) yield return x;
            all.Stop();
            Log.Message($"[PROBE] timed {count} ticks in {all.ElapsedMilliseconds} ms, frames {UnityEngine.Time.frameCount - frames}, gen0 {GC.CollectionCount(0) - gc}, gen2 {GC.CollectionCount(2)}, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB, pawns {map.mapPawns.AllPawnsSpawnedCount}, things {map.listerThings.AllThings.Count}");
            double perMs = Stopwatch.Frequency / 1000.0;
            Table("perf", new[] { "ms", "calls", "usPerCall", "owner", "patch", "target" },
                timers.Values.Where(t => t.calls > 0).OrderByDescending(t => t.stamps).Select(t => new object[] { (t.stamps / perMs).ToString("F1"), t.calls, (t.stamps / perMs * 1000 / t.calls).ToString("F2"), t.owner, t.patch, t.target }));
        }

        // ---- YART auto research ----

        static int autoDraws, autoClicks, autoFails, naturalFinishes;
        static UnityEngine.Vector2 autoCenter;
        static bool clickAuto;

        static void DumpLetters(string when)
        {
            foreach (Letter l in Find.LetterStack.LettersListForReading)
                Log.Message($"[PROBE] letter {when}: {l.GetUniqueLoadID()}, {l.GetType().Name}, {l.Label}, arrived {l.arrivalTick}, archived {Find.Archive.Contains(l)}");
            Log.Message($"[PROBE] archive {when}: {Find.Archive.ArchivablesListForReading.Count} entries");
        }

        // Stands in for the mouse: the button's own draw and click code runs, only the input is faked.
        static void ButtonPost(UnityEngine.Rect butRect, ref bool __result)
        {
            if (butRect.width != 110f || butRect.height != YART.Data.Constraints.QueueBarHeight - 24f) return;
            autoDraws++;
            autoCenter = UI.GUIToScreenPoint(butRect.center);
            if (!clickAuto) return;
            clickAuto = false;
            autoClicks++;
            __result = true;
        }

        static void FinishPost(ResearchProjectDef proj, Pawn researcher)
        {
            if (researcher != null) naturalFinishes++;
            Log.Message($"[PROBE] finished {proj.defName}, researcher {researcher?.LabelShort}, main thread {UnityData.IsInMainThread}, bench now {Find.ResearchManager.GetProject()?.defName}");
        }

        static void MessagePre(Message msg) => Log.Message("[PROBE] message: " + msg.text);

        static void Expect(bool ok, string what)
        {
            if (!ok) autoFails++;
            Log.Message($"[PROBE] {(ok ? "ok" : "FAIL")} {what}");
        }

        static bool IsGravtech(ResearchProjectDef def) => YART.Compat.GravshipCompat.IsGravshipResearchMethod != null && (bool)YART.Compat.GravshipCompat.IsGravshipResearchMethod.Invoke(null, new object[] { def });

        static List<ResearchProjectDef> Startable(YART.Data.ResearchChannel channel) =>
            YART.Data.ResearchGraph.Instance.AllNodes.Values.Where(n => n.Channel == channel && n.Def.CanStartNow && !IsGravtech(n.Def)).Select(n => n.Def).ToList();

        static void Shot(string label) => Guard("shot", () =>
        {
            Find.WindowStack.TryRemove(typeof(LudeonTK.EditWindow_Log), false);
            ScreenshotTaker.TakeNonSteamShot("LP17Probe-" + label);
            Log.Message("[PROBE-SHOT] " + label + " in " + GenFilePaths.ScreenshotFolderPath);
        });

        static int offStateScans;

        static void CheapestPre() { if (Current.ProgramState != ProgramState.Playing) offStateScans++; }

        static IEnumerable<int> ClickAuto()
        {
            int clicks = autoClicks;
            clickAuto = true;
            for (int i = 0; i < 600 && autoClicks == clicks; i++) yield return 1;
            clickAuto = false;
            Expect(autoClicks > clicks, "button took the click");
        }

        static IEnumerable<int> AutoResearchTest()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Harmony h = new Harmony("local.lp17.probe");
            h.Patch(AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonInvisible)), postfix: new HarmonyMethod(typeof(Probe), nameof(ButtonPost)));
            h.Patch(AccessTools.Method(typeof(ResearchManager), nameof(ResearchManager.FinishProject)), postfix: new HarmonyMethod(typeof(Probe), nameof(FinishPost)) { priority = Priority.Last });
            h.Patch(AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) }), prefix: new HarmonyMethod(typeof(Probe), nameof(MessagePre)));
            h.Patch(AccessTools.Method(typeof(YartQol.AutoResearch), "Cheapest"), prefix: new HarmonyMethod(typeof(Probe), nameof(CheapestPre)));
            YartQol.AutoResearch auto = Current.Game.GetComponent<YartQol.AutoResearch>();
            ResearchManager rm = Find.ResearchManager;
            YART.Data.ResearchQueueManager queue = YART.Data.ResearchQueueManager.Instance;
            MethodInfo missingMemes = AccessTools.Method(typeof(YART.Data.ResearchQueueManager), "ComputeUnlockedDefsThatHaveMissingMemes");
            bool NoDialog(ResearchProjectDef d) => ((System.Collections.ICollection)missingMemes.Invoke(null, new object[] { d })).Count == 0;

            Expect(auto != null, "component added to a save made without the mod");
            Expect(!auto.On, "off by default");

            Mark("open tab");
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research);
            while (!YART.GraphBuildPipeline.GraphReady) yield return 1;
            for (int i = 0; i < 60; i++) yield return 1;
            Expect(autoDraws > 0, $"button drawn {autoDraws} times, tab {MainButtonDefOf.Research.TabWindow.GetType().Name}");
            Find.WindowStack.TryRemove(typeof(LudeonTK.EditWindow_Log), false);
            Log.Message("[PROBE] windows: " + string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().FullName + " " + w.windowRect)));
            Expect(Find.WindowStack.GetWindowAt(autoCenter) is YART.MainTabWindow_YART, $"nothing covers the button at {autoCenter}: top window there is {Find.WindowStack.GetWindowAt(autoCenter)?.GetType().Name}");
            DumpLetters("before the test");
            Shot("off");
            yield return 1;

            Mark("player queue first");
            Log.Message($"[PROBE] save had bench project {rm.GetProject()?.defName}, channels {string.Join(",", YART.Data.ChannelRegistry.All.Select(c => c.Id))}, nodes {YART.Data.ResearchGraph.Instance.AllNodes.Count}, gravtech nodes {YART.Data.ResearchGraph.Instance.AllNodes.Keys.Count(IsGravtech)}");
            foreach (YART.Data.ResearchChannel c in YART.Data.ChannelRegistry.All)
            {
                queue.ClearQueue(c);
                if (c.CurrentProject != null) rm.StopProject(c.CurrentProject);
                Log.Message($"[PROBE] channel {c.Id}: {Startable(c).Count} can start now");
            }
            List<ResearchProjectDef> bench = Startable(YART.Data.ChannelRegistry.Bench).Where(NoDialog).OrderBy(d => d.CostApparent).ToList();
            ResearchProjectDef first = bench[bench.Count - 1], second = bench[bench.Count - 2];
            queue.EnqueueWithChain(first);
            queue.EnqueueWithChain(second);
            Expect(rm.GetProject() == first, "queued project starts: " + first.defName);
            foreach (int x in ClickAuto()) yield return x;
            Expect(auto.On, "click turns it on");
            Expect(rm.GetProject() == first, "it leaves the running queued project alone");
            yield return 1;
            Shot("on, queue of two");
            yield return 1;
            rm.FinishProject(first, doCompletionDialog: true);
            Expect(rm.GetProject() == second, "next queued project wins over the cheapest: " + rm.GetProject()?.defName);
            rm.FinishProject(second, doCompletionDialog: true);
            float cheapest = Startable(YART.Data.ChannelRegistry.Bench).Min(d => d.CostApparent);
            Expect(rm.GetProject() != null && rm.GetProject().CostApparent <= cheapest + 0.01f, $"queue empty, cheapest picked at once: {rm.GetProject()?.defName} at {rm.GetProject()?.CostApparent} (cheapest {cheapest})");
            Expect(queue.GetQueue(YART.Data.ChannelRegistry.Bench).Contains(rm.GetProject()), "pick shows in YART's queue bar");
            yield return 1;

            Mark("pick cost");
            Stopwatch pick = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) auto.Pick();
            Log.Message($"[PROBE] Pick with the bench busy: {pick.Elapsed.TotalMilliseconds / 200:F3} ms each");
            yield return 1;

            Mark("save and reload");
            Find.MainTabsRoot.EscapeCurrentTab(false);
            Game old = Current.Game;
            ResearchProjectDef running = rm.GetProject();
            DumpLetters("before the save");
            GameDataSaveLoader.SaveGame("LP17ProbeTemp");
            GameDataSaveLoader.LoadGame("LP17ProbeTemp");
            while (Current.Game == old || Current.ProgramState != ProgramState.Playing) yield return 1;
            for (int i = 0; i < 60; i++) yield return 1;
            File.Delete(GenFilePaths.FilePathForSavedGame("LP17ProbeTemp"));
            auto = Current.Game.GetComponent<YartQol.AutoResearch>();
            rm = Find.ResearchManager;
            queue = YART.Data.ResearchQueueManager.Instance;
            Expect(auto.On, "still on after a reload");
            Expect(rm.GetProject() == running, "same project after a reload");
            Expect(offStateScans == 0, $"no scan while loading ({offStateScans})");

            Mark("natural");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            Stopwatch natural = Stopwatch.StartNew();
            while (natural.Elapsed.TotalSeconds < 150 && naturalFinishes < 2)
            {
                ResearchProjectDef cur = rm.GetProject();
                if (cur != null && cur.ProgressPercent < 0.99f) rm.AddProgress(cur, cur.baseCost * 0.995f - cur.ProgressReal);
                yield return 1;
            }
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Log.Message($"[PROBE] natural: {naturalFinishes} finished by a pawn in {natural.Elapsed.TotalSeconds:F0} s, bench now {rm.GetProject()?.defName}");

            Mark("run out");
            List<object[]> picks = new List<object[]>();
            for (int round = 0; auto.On && round < 2000; round++)
            {
                bool any = false;
                foreach (YART.Data.ResearchChannel c in YART.Data.ChannelRegistry.All)
                {
                    ResearchProjectDef cur = c.CurrentProject;
                    if (cur == null) continue;
                    any = true;
                    picks.Add(new object[] { round, c.Id, cur.defName, cur.CostApparent, cur.tab?.defName });
                    rm.FinishProject(cur, doCompletionDialog: true);
                    ResearchProjectDef next = c.CurrentProject;
                    List<ResearchProjectDef> open = Startable(c);
                    if (next == null && open.Count > 0) Expect(false, $"{c.Id} idle after {cur.defName} with {open.Count} startable, first {open[0].defName}");
                    if (next != null && open.Count > 0 && next.CostApparent > open.Min(d => d.CostApparent) + 0.01f) Expect(false, $"{next.defName} is not the cheapest on {c.Id}");
                }
                if (!any) { Expect(false, "still on with nothing running"); break; }
                yield return 0;
            }
            Expect(!auto.On, $"turned itself off after {picks.Count} projects");
            foreach (YART.Data.ResearchChannel c in YART.Data.ChannelRegistry.All)
                Expect(Startable(c).Count == 0 && c.CurrentProject == null, $"nothing left to start on {c.Id}");
            Table("picks", new[] { "round", "channel", "project", "cost", "tab" }, picks);
            List<string> reasons = new List<string>();
            Table("remaining", new[] { "project", "channel", "gravtech", "canStartNow", "reasons" }, YART.Data.ResearchGraph.Instance.AllNodes.Values.Where(n => !n.Def.IsFinished).Select(n =>
            {
                n.GetLockedReasons(reasons);
                return new object[] { n.Def.defName, n.Channel.Id, IsGravtech(n.Def), n.Def.CanStartNow, string.Join("; ", reasons) };
            }));

            Mark("click with nothing left");
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research);
            for (int i = 0; i < 60; i++) yield return 1;
            foreach (int x in ClickAuto()) yield return x;
            Expect(!auto.On, "stays off when nothing is available");
            yield return 1;
            Shot("off, nothing left");
            for (int i = 0; i < 30; i++) yield return 1;
            Log.Message($"[PROBE] auto research failures: {autoFails}");
        }

        // ---- Hemogen Tap ----

        static string tapMessage;

        static void TapMessagePre(Message msg) => tapMessage = msg.text;

        static float BloodLoss(Pawn pawn) => pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;

        // Counts stacked items, since a pack placed Near merges into a stack that has room.
        static int Packs(Map map) => map.listerThings.ThingsOfDef(ThingDefOf.HemogenPack).Sum(t => t.stackCount);

        static IEnumerable<int> BS9000Test()
        {
            bool active = ModsConfig.IsActive("local.hemogen.tap");
            Expect(active, "mod local.hemogen.tap is active");
            // The mod's types are only used inside BS9000Run, so an inactive mod ends the run with a FAIL instead of a step that throws.
            if (active) foreach (int x in BS9000Run()) yield return x;
            Log.Message($"[PROBE] hemogen tap failures: {autoFails}");
        }

        static IEnumerable<int> BS9000Run()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            new Harmony("local.lp17.probe").Patch(AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) }), prefix: new HarmonyMethod(typeof(Probe), nameof(TapMessagePre)));
            HediffDef tap = HediffDef.Named("BS9000");
            Expect(tap != null, "hediff def BS9000 is loaded");
            if (tap == null) yield break;

            Mark("pick colonist");
            Pawn pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault(x => !x.Dead && x.IsColonistPlayerControlled && !x.DevelopmentalStage.Baby() && x.health.CanBleed && BloodLoss(x) < 0.4f);
            Expect(pawn != null, "a free colonist who can bleed to test on");
            if (pawn == null) yield break;
            Map map = pawn.MapHeld;
            float loss = BloodLoss(pawn);
            int packs = Packs(map);

            Mark("research");
            ResearchProjectDef proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("BS9000");
            Expect(proj != null, "research project BS9000 exists");
            Expect(proj?.prerequisites != null && proj.prerequisites.Any(x => x.defName == "Bionics"), "research project BS9000 needs Bionics");
            RecipeDef make = DefDatabase<RecipeDef>.GetNamedSilentFail("Make_Bloodsucker");
            Expect(make != null && proj != null && make.researchPrerequisite == proj, "recipe Make_Bloodsucker needs research project BS9000");
            // ThingDefOf has no FabricationBench field, so the bench is matched by name.
            Expect(make?.recipeUsers != null && make.recipeUsers.Any(x => x.defName == "FabricationBench"), "recipe Make_Bloodsucker is made at the fabrication bench");
            Expect(proj != null && proj.UnlockedDefs.OfType<ThingDef>().Any(x => x.defName == "Bloodsucker"), "research project BS9000 unlocks the hemogen tap");
            yield return 1;

            Mark("install");
            pawn.health.AddHediff(tap, pawn.RaceProps.body.AllParts.First(x => x.def.defName == "Torso"));
            BS9000.HediffComp_BS9000 comp = pawn.health.hediffSet.GetFirstHediffOfDef(tap)?.TryGetComp<BS9000.HediffComp_BS9000>();
            Expect(comp != null, "implant is present and carries the tap comp");
            if (comp == null) yield break;
            yield return 1;

            Mark("toggle");
            // The base CompGetGizmos returns null, so a comp without the override reads as zero toggles.
            List<Command_Toggle> toggles = (comp.CompGetGizmos() ?? Enumerable.Empty<Gizmo>()).OfType<Command_Toggle>().ToList();
            Expect(toggles.Count == 1, "tap comp offers exactly one toggle");
            if (toggles.Count == 0) yield break;
            Command_Toggle toggle = toggles[0];
            Expect(toggle.icon != null, "toggle has an icon");
            Expect(ContentFinder<UnityEngine.Texture2D>.Get("UI/Icons/Medical/Bleeding", false) != null, "Core bleeding icon loads");
            Expect(toggle.isActive(), "toggle starts on");
            List<Command_Toggle> shown = new List<Command_Toggle>();
            Guard("pawn gizmos", () => shown = pawn.GetGizmos().OfType<Command_Toggle>().ToList());
            Expect(shown.Any(x => x.defaultLabel == toggle.defaultLabel), "pawn gizmos include the toggle");
            toggle.toggleAction();
            Expect(!toggle.isActive(), "toggle turns off");
            tapMessage = null;
            float none = 0f;
            comp.CompPostTickInterval(ref none, 60000);
            float lossOff = BloodLoss(pawn);
            int packsOff = Packs(map);
            Log.Message($"[PROBE] hemogen tap toggle: label '{toggle.defaultLabel}', {shown.Count} toggles on {pawn.LabelShort} (player controlled: {pawn.IsColonistPlayerControlled}), tick while off: packs {packs} -> {packsOff}, blood loss {loss:F3} -> {lossOff:F3}, message: {tapMessage}");
            Expect(packsOff == packs && Math.Abs(lossOff - loss) < 0.0001f && tapMessage == null, "tick while off: no pack, no blood loss, no message");
            toggle.toggleAction();
            Expect(toggle.isActive(), "toggle turns back on");
            yield return 1;

            Mark("first tick");
            comp.CompPostTickInterval(ref none, 60000);
            float lossAfter = BloodLoss(pawn);
            int packsAfter = Packs(map);
            Log.Message($"[PROBE] hemogen tap on {pawn.LabelShort}: packs {packs} -> {packsAfter}, blood loss {loss:F3} -> {lossAfter:F3}, message: {tapMessage}");
            Expect(packsAfter == packs + 1, "one pack dropped");
            Expect(map.listerThings.ThingsOfDef(ThingDefOf.HemogenPack).Any(t => t.Position.InHorDistOf(pawn.PositionHeld, 3f)), "a pack lies within 3 cells of the pawn");
            Expect(Math.Abs(lossAfter - 0.44f) < 0.01f, "blood loss capped at 0.44 after the tap");
            Expect(tapMessage != null && tapMessage.IndexOf(pawn.LabelShort, StringComparison.OrdinalIgnoreCase) >= 0, "message names the pawn");
            yield return 1;

            Mark("second tick");
            tapMessage = null;
            comp.CompPostTickInterval(ref none, 60000);
            Expect(Packs(map) == packsAfter, "second tick: no new pack");
            Expect(Math.Abs(BloodLoss(pawn) - lossAfter) < 0.0001f, "second tick: blood loss unchanged");
            BS9000.HediffComp_BS9000.Tap(pawn);
            Expect(Packs(map) == packsAfter && Math.Abs(BloodLoss(pawn) - lossAfter) < 0.0001f && tapMessage == null, "direct Tap is skipped: no pack, no blood loss, no message");
            yield return 1;

            pawn.health.RemoveHediff(pawn.health.hediffSet.GetFirstHediffOfDef(tap));
        }

        // ---- Hemogen Tap promo scene ----

        // Set once the scene hides the UI, so Frame() keeps closing the windows the game opens meanwhile, like the debug log on an error.
        static bool filming;

        static IEnumerable<int> Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return 1;
        }

        // Waits until done() is true, at most n frames. The caller checks which of the two ended it.
        static IEnumerable<int> WaitFor(Func<bool> done, int n)
        {
            for (int i = 0; i < n && !done(); i++) yield return 1;
        }

        static void Teleport(Pawn pawn, IntVec3 cell, Rot4? rot = null)
        {
            pawn.Position = cell;
            pawn.Notify_Teleported(true, true);
            if (rot.HasValue) pawn.Rotation = rot.Value;
        }

        static void Draft(Pawn pawn, bool on)
        {
            if (pawn.drafter != null) pawn.drafter.Drafted = on;
        }

        static bool Walk(Pawn pawn, IntVec3 cell) => pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, cell), JobTag.Misc);

        static void HideUi()
        {
            filming = true;
            AccessTools.Field(typeof(LudeonTK.EditWindow_Log), "canAutoOpen")?.SetValue(null, false); // else the next error opens the debug log over the shot
            Find.UIRoot.screenshotMode.Active = true;
            Find.WindowStack.TryRemove(typeof(LudeonTK.EditWindow_Log), false);
            foreach (Window w in Find.WindowStack.Windows.ToList()) w.Close(false);
            Find.Selector.ClearSelection();
        }

        // The stage is a cross of open ground, 20 cells up and down and 30 left and 12 right of c, with room for a bed 8 cells to the right.
        static bool StageFits(Map map, IntVec3 c, bool strict)
        {
            bool Open(IntVec3 p) => p.InBounds(map) && p.Standable(map) && !p.Fogged(map) && (!strict || (!p.Roofed(map) && p.GetEdifice(map) == null));
            for (int d = -20; d <= 20; d++) if (!Open(c + new IntVec3(0, 0, d))) return false;
            for (int d = -30; d <= 12; d++) if (!Open(c + new IntVec3(d, 0, 0))) return false;
            IntVec3 bed = c + new IntVec3(8, 0, 0);
            return GenConstruct.CanPlaceBlueprintAt(ThingDefOf.Bed, bed, Rot4.South, map).Accepted
                && (bed + IntVec3.North).Standable(map) && (bed + IntVec3.West).Standable(map) && (bed + IntVec3.East).Standable(map);
        }

        // Scans every second cell inside a 30-cell margin, which also keeps the whole cross on the map, and takes the fitting cell nearest to near.
        static IntVec3 FindStage(Map map, IntVec3 near, bool strict)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;
            for (int x = 30; x < map.Size.x - 30; x += 2)
                for (int z = 30; z < map.Size.z - 30; z += 2)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    int dist = (c - near).LengthHorizontalSquared;
                    if (dist < bestDist && StageFits(map, c, strict)) { best = c; bestDist = dist; }
                }
            return best;
        }

        static IEnumerable<int> Bs9000Gif()
        {
            bool active = ModsConfig.IsActive("local.hemogen.tap");
            Expect(active, "mod local.hemogen.tap is active");
            // The mod's type is only named inside Bs9000GifRun, so an inactive mod ends the run with a FAIL instead of a step that throws.
            if (active) foreach (int x in Bs9000GifRun()) yield return x;
            Log.Message("[PROBE] bs9000gif failures: " + autoFails);
        }

        // Cut 1 zooms in on the host's face. Cut 2 has the host walk in, tap and walk out. Cut 3 has the doctor take the dropped pack to the patient in the bed.
        // The recording script trims by the timestamps of the cut lines, so their text stays exactly as it is, and no wait throws: a timeout is a FAIL and the scene goes on.
        static IEnumerable<int> Bs9000GifRun()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Map map = Find.CurrentMap;

            Mark("pick colonists");
            List<Pawn> free = map.mapPawns.FreeColonistsSpawned.Where(c => !c.Dead && !c.Downed && c.DevelopmentalStage.Adult()).ToList();
            Expect(free.Count >= 3, $"three free adult colonists for host, doctor and patient (found {free.Count})");
            if (free.Count == 0) yield break;
            Pawn host = free[0];
            List<Pawn> others = free.Skip(1).ToList();
            Pawn doctor = others.OrderByDescending(c => c.skills.GetSkill(SkillDefOf.Medicine).Level).FirstOrDefault() ?? host;
            Pawn patient = others.FirstOrDefault(c => c != doctor) ?? host;
            Log.Message($"[PROBE] host {host.LabelShort}, doctor {doctor.LabelShort}, patient {patient.LabelShort}");

            Mark("stage cell");
            IntVec3 near = new IntVec3((host.Position.x + doctor.Position.x + patient.Position.x) / 3, 0, (host.Position.z + doctor.Position.z + patient.Position.z) / 3);
            IntVec3 stage = FindStage(map, near, true);
            if (!stage.IsValid) stage = FindStage(map, near, false);
            Expect(stage.IsValid, "a stage cell with a clear cross of open ground and room for a bed");
            if (!stage.IsValid) yield break;
            IntVec3 bedCell = stage + new IntVec3(8, 0, 0);
            Log.Message($"[PROBE] stage cell {stage}, bed spot {bedCell}");

            Mark("daylight and weather");
            for (int i = 0; i < 24 && GenLocalDate.HourInteger(map) is < 11 or > 14; i++)
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 2500);
            map.weatherManager.curWeather = WeatherDefOf.Clear;
            map.weatherManager.lastWeather = WeatherDefOf.Clear;
            map.weatherManager.curWeatherAge = 0; // the weather decider changes the weather once its age passes the planned duration

            Mark("draft and park");
            // Nobody else may wander into the shot or haul the pack. The doctor stays drafted, which keeps them where they are, until their order in cut 3:
            // a Goto to their own cell ends at once and an undrafted colonist would go back to work.
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToList())
                if (p != host && p != doctor && p != patient && !p.Downed) Draft(p, true);
            Draft(host, true);
            doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 20;
            Draft(doctor, true);
            Teleport(doctor, stage + new IntVec3(-30, 0, 0));
            // The patient gets up again unless some colonist other than the patient has doctoring switched on.
            if (doctor.workSettings != null && doctor.workSettings.EverWork && !doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor)) doctor.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);

            Mark("bed and patient");
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("BloodTransfusion");
            Expect(recipe != null, "recipe BloodTransfusion exists");
            if (recipe == null) yield break;
            Building_Bed bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, bedCell, map, Rot4.South);
            bed.Medical = true;
            Teleport(patient, bed.Position);
            HealthUtility.AdjustSeverity(patient, HediffDefOf.BloodLoss, 0.5f - BloodLoss(patient));
            Draft(patient, false);
            // StartJob checks the medical bed at once. LayDown fails there unless a surgery is due and another awake colonist has doctoring on (set up above), so the bill goes in first.
            Bill_Medical bill = new Bill_Medical(recipe, null);
            patient.health.surgeryBills.AddBill(bill);
            Job lieDown = JobMaker.MakeJob(JobDefOf.LayDown, bed);
            lieDown.restUntilHealed = true;
            patient.jobs.StartJob(lieDown, JobCondition.InterruptForced);

            Mark("hide ui and settle");
            HideUi();
            Teleport(host, stage, Rot4.South);
            Find.CameraDriver.config.sizeRange.min = 1.5f; // vanilla's minimum zoom of 11 puts the camera lower than the pawn layer at close sizes and pulls the zoom back out during the hold
            // Cut 1's opening view is set before the cut1 start line is logged, so a frame grabbed just before that line already shows it.
            Find.CameraDriver.JumpToCurrentMapLoc(host.DrawPos + new UnityEngine.Vector3(0f, 0f, 0.3f));
            Find.CameraDriver.SetRootSize(20f);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            foreach (int x in Frames(90)) yield return x;
            foreach (int x in WaitFor(() => patient.InBed(), 600)) yield return x; // only when a fast machine fitted fewer ticks into 90 frames
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Expect(patient.InBed(), "patient lies in the bed");
            Draft(host, false); // paused, so the host stays put; undrafted they hold no weapon in the face shot

            Mark("cut 1");
            Log.Message("[PROBE] cut1 start");
            for (int i = 0; i < 240; i++)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(host.DrawPos + new UnityEngine.Vector3(0f, 0f, 0.3f));
                Find.CameraDriver.SetRootSize(UnityEngine.Mathf.Lerp(20f, 2.2f, UnityEngine.Mathf.SmoothStep(0f, 1f, i / 239f)));
                yield return 1;
            }
            foreach (int x in Frames(45)) yield return x;
            Log.Message("[PROBE] cut1 end");

            Mark("cut 2");
            Find.CameraDriver.JumpToCurrentMapLoc(stage);
            Find.CameraDriver.SetRootSize(9f);
            Teleport(host, stage + new IntVec3(0, 0, -20), Rot4.North);
            Draft(host, true);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Log.Message("[PROBE] cut2 start");
            Expect(Walk(host, stage), "host takes the order to walk to the stage cell");
            foreach (int x in WaitFor(() => host.Position == stage, 900)) yield return x;
            Expect(host.Position == stage, "host reaches the stage cell");
            Mark("tap");
            Guard("tap", () => BS9000.HediffComp_BS9000.Tap(host));
            foreach (int x in Frames(45)) yield return x;
            Mark("walk out");
            Walk(host, stage + new IntVec3(0, 0, 20));
            foreach (int x in WaitFor(() => host.Position.z >= stage.z + 18, 900)) yield return x;
            Expect(host.Position.z >= stage.z + 18, "host walks out past the top edge");
            Log.Message("[PROBE] cut2 end");

            Mark("cut 3");
            Log.Message("[PROBE] cut3 start");
            Thing pack = map.listerThings.ThingsOfDef(ThingDefOf.HemogenPack).Where(t => t.Position.InHorDistOf(stage, 4f)).OrderBy(t => t.Position.DistanceToSquared(stage)).FirstOrDefault();
            Expect(pack != null, "a hemogen pack lies within 4 cells of the stage cell");
            if (pack == null)
            {
                pack = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.HemogenPack), stage, map);
                Log.Message("[PROBE] no pack from the tap, spawned one at the stage cell");
            }
            // The job vanilla gives a doctor for a surgery bill, see WorkGiver_DoBill.TryStartNewDoBillJob.
            Job transfuse = JobMaker.MakeJob(JobDefOf.DoBill, patient);
            transfuse.targetQueueB = new List<LocalTargetInfo> { pack };
            transfuse.countQueue = new List<int> { 1 };
            transfuse.haulMode = HaulMode.ToCellNonStorage;
            transfuse.bill = bill;
            Draft(doctor, false);
            IntVec3 packCell = pack.Position;
            Expect(doctor.jobs.TryTakeOrderedJob(transfuse, JobTag.Misc), "doctor takes the transfusion order");
            // The long walks run at Fast; the pickup and the arrival at the bed at Normal.
            for (int i = 0; i < 1200 && !doctor.Position.InHorDistOf(bedCell, 2f); i++)
            {
                bool far = !doctor.Position.InHorDistOf(packCell, 6f) && !doctor.Position.InHorDistOf(bedCell, 6f);
                Find.TickManager.CurTimeSpeed = far ? TimeSpeed.Fast : TimeSpeed.Normal;
                yield return 1;
            }
            Expect(doctor.Position.InHorDistOf(bedCell, 2f), "doctor reaches the bed");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Fast;
            foreach (int x in WaitFor(() => BloodLoss(patient) < 0.3f || !patient.health.surgeryBills.Bills.Contains(bill), 1800)) yield return x;
            Expect(BloodLoss(patient) < 0.3f, $"blood loss dropped below 0.3 (now {BloodLoss(patient):F2})");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            foreach (int x in Frames(45)) yield return x;
            Log.Message("[PROBE] cut3 end");
        }

        static IEnumerable<int> Gear()
        {
            Map map = Find.AnyPlayerHomeMap;
            using (StreamWriter w = new StreamWriter(outPath + ".gear.tsv"))
            {
                w.WriteLine("faction\tkind\tgroup\tgender\tbody\tweapon\tapparel");
                void Record(RimWorld.FactionDef f, Pawn p, string group)
                {
                    w.WriteLine(string.Join("\t", new object[] { f.defName, p.kindDef.defName, group, p.gender, p.story?.bodyType?.defName, p.equipment?.Primary?.def.defName, Join(p.apparel?.WornApparel.Select(a => a.def.defName)) }.Select(Cell)));
                }
                foreach (RimWorld.FactionDef f in DefDatabase<RimWorld.FactionDef>.AllDefs.ToList())
                {
                    if (!f.humanlikeFaction || f.isPlayer || f.pawnGroupMakers.NullOrEmpty()) continue;
                    Mark("faction " + f.defName);
                    Faction fac = Find.FactionManager.FirstFactionOfDef(f);
                    Guard("make " + f.defName, () => { if (fac == null) fac = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(f)); });
                    if (fac == null) continue;
                    foreach (PawnKindDef k in f.pawnGroupMakers.SelectMany(g => g.options.Concat(g.traders ?? new List<PawnGenOption>()).Concat(g.guards ?? new List<PawnGenOption>())).Select(o => o.kind).Distinct().ToList())
                    {
                        if (k?.race == null || !k.RaceProps.Humanlike) continue;
                        for (int i = 0; i < 6; i++)
                            Guard("pawn " + f.defName + "/" + k.defName, () =>
                            {
                                Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(k, fac, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
                                Record(f, p, "kind");
                                p.Discard(true);
                            });
                        yield return 0;
                    }
                    if (f.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Peaceful))
                        for (int i = 0; i < 4; i++)
                        {
                            Guard("visitors " + f.defName, () =>
                            {
                                foreach (Pawn p in PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Peaceful, tile = map.Tile, faction = fac, points = 500f }, false).ToList())
                                {
                                    Record(f, p, "visitor");
                                    p.Discard(true);
                                }
                            });
                            yield return 0;
                        }
                }
            }
        }

        static readonly Dictionary<string, int> noIcon = new Dictionary<string, int>();

        static IEnumerable<int> Soak()
        {
            Map map = Find.AnyPlayerHomeMap;
            Mark("ui tabs");
            foreach (MainButtonDef b in DefDatabase<MainButtonDef>.AllDefsListForReading)
            {
                if (b.TabWindow == null) continue;
                Mark("tab " + b.defName);
                Guard("tab " + b.defName, () => Find.MainTabsRoot.SetCurrentTab(b, false));
                yield return 1; yield return 1; yield return 1;
            }
            Pawn colonist = map.mapPawns.FreeColonists.FirstOrDefault();
            if (colonist != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(colonist, false, false);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, false);
                foreach (InspectTabBase tab in colonist.GetInspectTabs().ToList())
                {
                    Mark("itab " + tab.GetType().Name);
                    Guard("itab " + tab.GetType().Name, () => InspectPaneUtility.OpenTab(tab.GetType()));
                    yield return 1; yield return 1; yield return 1;
                }
            }
            Find.MainTabsRoot.EscapeCurrentTab(false);

            Mark("defs");
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading.ToList())
            {
                Guard("def " + d.defName, () =>
                {
                    if (d.category != ThingCategory.Item && d.category != ThingCategory.Building) return;
                    if (d.IsBlueprint || d.IsFrame || d.IsCorpse || typeof(MinifiedThing).IsAssignableFrom(d.thingClass)) return;
                    ThingDef stuff = d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null;
                    _ = d.SpecialDisplayStats(StatRequest.For(d, stuff)).ToList();
                    _ = d.LabelCap; _ = d.uiIcon; _ = d.graphic;
                    Thing t = ThingMaker.MakeThing(d, stuff);
                    _ = t.DescriptionFlavor;
                    _ = t.SpecialDisplayStats().ToList();
                    _ = t.MarketValue;
                });
                yield return 0;
            }
            foreach (HediffDef d in DefDatabase<HediffDef>.AllDefsListForReading.ToList())
            {
                Guard("hediff " + d.defName, () => { _ = d.SpecialDisplayStats(StatRequest.ForEmpty()).ToList(); });
                yield return 0;
            }
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading.ToList())
            {
                Guard("recipe " + r.defName, () => { _ = r.LabelCap; _ = r.WorkAmountTotal(null); _ = r.AvailableNow; _ = r.ProducedThingDef; _ = r.PotentiallyMissingIngredients(null, map).ToList(); });
                yield return 0;
            }

            Mark("pawn kinds");
            foreach (PawnKindDef k in DefDatabase<PawnKindDef>.AllDefsListForReading.ToList())
            {
                Mark("kind " + k.defName);
                Guard("kind " + k.defName, () =>
                {
                    Pawn p = PawnGenerator.GeneratePawn(k, null);
                    _ = p.LabelCap;
                });
                yield return 1;
            }

            Mark("spawn");
            List<Thing> spawned = new List<Thing>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading.ToList())
            {
                if (d.category != ThingCategory.Item && d.category != ThingCategory.Building) continue;
                if (d.IsBlueprint || d.IsFrame || d.IsCorpse || d.thingClass == null || d.destroyOnDrop || d.IsFilth || skip.Contains(d.defName)) continue;
                if (typeof(MinifiedThing).IsAssignableFrom(d.thingClass) || typeof(PocketMapExit).IsAssignableFrom(d.thingClass) || d.defName.StartsWith("VFEPD_")) continue;
                Mark("spawn " + d.defName);
                Guard("spawn " + d.defName, () =>
                {
                    if (!CellFinderLoose.TryGetRandomCellWith(c => c.Standable(map) && c.GetFirstBuilding(map) == null && GenAdj.OccupiedRect(c, Rot4.North, d.size).InBounds(map), map, 200, out IntVec3 cell)) return;
                    Thing t = ThingMaker.MakeThing(d, d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null);
                    if (t.def.CanHaveFaction && d.category == ThingCategory.Building) t.SetFactionDirect(Faction.OfPlayer);
                    GenSpawn.Spawn(t, cell, map, WipeMode.Vanish);
                    spawned.Add(t);
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(t, false, false);
                    _ = t.LabelCap;
                    _ = t.GetInspectString();
                    foreach (Gizmo g in t.GetGizmos())
                        if (g is Command c && c.icon == null && !(g is Designator))
                        {
                            string key = c.GetType().FullName + " '" + c.defaultLabel + "'";
                            noIcon.TryGetValue(key, out int n);
                            noIcon[key] = n + 1;
                        }
                    _ = t.GetInspectTabs()?.ToList();
                });
                yield return 0;
            }
            Log.Message("[PROBE] commands with no icon: " + string.Join("; ", noIcon.OrderByDescending(p => p.Value).Select(p => p.Key + " x" + p.Value)));
            Mark("spawn ticks");
            foreach (int x in Ticks(600)) yield return x;
            Mark("spawn cleanup");
            foreach (Thing t in spawned)
            {
                Guard("destroy " + t.def.defName, () => { if (!t.Destroyed && t.def.destroyable) t.Destroy(); });
                yield return 0;
            }

            Mark("incidents");
            foreach (IncidentDef d in DefDatabase<IncidentDef>.AllDefsListForReading.ToList())
            {
                Mark("incident " + d.defName);
                Guard("incident " + d.defName, () =>
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(d.category, map);
                    if (d.Worker.CanFireNow(parms)) d.Worker.TryExecute(parms);
                });
                foreach (int x in Ticks(30)) yield return x;
            }

            Mark("quests");
            foreach (QuestScriptDef q in DefDatabase<QuestScriptDef>.AllDefsListForReading.ToList())
            {
                if (q.rootSelectionWeight <= 0f) continue;
                Mark("quest " + q.defName);
                Guard("quest " + q.defName, () =>
                {
                    float points = StorytellerUtility.DefaultThreatPointsNow(map);
                    if (q.CanRun(points, map)) QuestUtility.GenerateQuestAndMakeAvailable(q, points);
                });
                foreach (int x in Ticks(30)) yield return x;
            }

            Mark("regressions");
            Guard("firefoam on a thing that never burned", () =>
            {
                Thing unburned = map.listerThings.AllThings.First(t => t.TryGetComp<CompAttachBase>() is CompAttachBase c && c.attachments == null);
                Log.Message("[PROBE] firefoam on " + unburned);
                DamageDefOf.Extinguish.Worker.Apply(new DamageInfo(DamageDefOf.Extinguish, 1f), unburned);
            });
            Guard("hive search for a pawn with no map", () =>
            {
                object hive = AccessTools.Method(typeof(LordToil_HiveRelated), "FindClosestHive").Invoke(new LordToil_DefendAndExpandHive(), new object[] { PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist) });
                Log.Message("[PROBE] hive for a pawn with no map: " + (hive ?? "none"));
            });

            Mark("long ticks");
            foreach (int x in Ticks(6000)) yield return x;
        }
    }
}
