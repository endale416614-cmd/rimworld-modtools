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
//   landmark : needs Odyssey. On the current map's tile, open the tile editor from the "Edit tile" gizmo, check both tabs list every landmark def and every tile mutator def with their defNames, let it draw, set a deterministic landmark, check its required mutators land on the tile, check validity is still judged on an empty tile, remove the landmark and check every mutator it added is stripped, then remove and restore a world-generated feature and add and remove a modded one, close the window and restore the old landmark in memory. Saves nothing.
//   sitepage : from the main menu, open the real Select Starting Site page of a new Crashlanded game, check the Edit tile gizmo is in the world gizmo grid drawn on that page, and screenshot it. Saves nothing and creates no colony.
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
            else if (mode == "sitepage") new Harmony("local.lp17.probe").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Probe), nameof(SitePageFrame)));
            else if (mode == "soak" || mode == "gear" || mode == "scan" || mode == "autoresearch" || mode == "hemogentap" || mode == "bs9000gif" || mode == "newgame" || mode == "select" || mode == "conflicts" || mode == "landmark" || mode == "aspiration")
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
            if (run == null) { Log.Message("[PROBE] playing"); run = (mode == "gear" ? Gear() : mode == "scan" ? Scan() : mode == "autoresearch" ? AutoResearchTest() : mode == "hemogentap" ? BS9000Test() : mode == "bs9000gif" ? Bs9000Gif() : mode == "newgame" ? NewGameTest() : mode == "select" ? SelectTest() : mode == "conflicts" ? ConflictsTest() : mode == "landmark" ? LandmarkTest() : mode == "aspiration" ? AspirationTest() : Soak()).GetEnumerator(); }
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
            // The fourth speed the time controls offer. Its multiplier is read from the tps this logs, not assumed.
            foreach (int x in Natural("ultra", TimeSpeed.Ultrafast, 30)) yield return x;
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
            // A save with auto research already on cannot show the default, and the faked click below would turn it off and
            // leave every pick check with nothing to watch. Toggle only flips the flag off, so no scan runs here.
            if (auto.On) { auto.Toggle(); Log.Message("[PROBE] save had auto research on; turned it off for the test"); }
            Expect(!auto.On, "off before the click");

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
            if (active) foreach (int x in BS9000GearRun()) yield return x;
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

        // Spawns a throwaway adult to drain or to treat. Everything it touches lives in memory only.
        static Pawn SpawnTestSubject(Map map, IntVec3 near)
        {
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                PawnKindDef.Named("Villager"), null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                developmentalStages: DevelopmentalStage.Adult));
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(near, map, 6), map);
            return p;
        }

        // ---- BS-7500 Drain Lance and BG-9555 Hemogen Tank ----

        static IEnumerable<int> BS9000GearRun()
        {
            Mark("gear defs");
            ThingDef lanceDef = DefDatabase<ThingDef>.GetNamedSilentFail("BS7500_DrainLance");
            ThingDef tankDef = DefDatabase<ThingDef>.GetNamedSilentFail("BG9555_HemogenTank");
            Expect(lanceDef != null, "thing def BS7500_DrainLance is loaded");
            Expect(tankDef != null, "thing def BG9555_HemogenTank is loaded");
            if (lanceDef == null || tankDef == null) yield break;

            ResearchProjectDef proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("BS9000");
            foreach (ThingDef d in new[] { lanceDef, tankDef })
            {
                RecipeDef r = DefDatabase<RecipeDef>.GetNamedSilentFail("Make_" + d.defName);
                Expect(r != null, "recipe Make_" + d.defName + " exists");
                Expect(r != null && r.researchPrerequisite == proj, "recipe Make_" + d.defName + " needs research project BS9000");
                Expect(r?.recipeUsers != null && r.recipeUsers.Any(x => x.defName == "FabricationBench"), "recipe Make_" + d.defName + " is made at the fabrication bench");
                Expect(proj != null && proj.UnlockedDefs.Contains(d), "research project BS9000 unlocks " + d.defName);
            }
            yield return 1;

            Mark("gear slots");
            Expect(lanceDef.apparel.layers.Any(x => x.defName == "Belt") && lanceDef.apparel.bodyPartGroups.Any(x => x.defName == "Waist"),
                "lance sits on the utility layer at the waist");
            Expect(tankDef.apparel.layers.Any(x => x.defName == "Belt") && tankDef.apparel.bodyPartGroups.Any(x => x.defName == "Torso"),
                "tank sits on the utility layer at the torso");
            Expect(ApparelUtility.CanWearTogether(lanceDef, tankDef, BodyDefOf.Human), "lance and tank can be worn together");
            yield return 1;

            Mark("gear textures");
            // Ground textures are Graphic_Single and need the base file only. Worn overlays are read
            // as a Graphic_Multi, so those need every direction on disk. The lance is worn from its
            // own item texture, as the vanilla lances are, so that path is in both lists.
            foreach (string path in new[] { lanceDef.graphicData.texPath, tankDef.graphicData.texPath })
                Expect(ContentFinder<UnityEngine.Texture2D>.Get(path, false) != null, "ground texture loads: " + path);

            foreach (string path in new[] { lanceDef.apparel.wornGraphicPath, tankDef.apparel.wornGraphicPath })
            {
                Expect(ContentFinder<UnityEngine.Texture2D>.Get(path, false) != null, "worn texture loads: " + path);
                foreach (string dir in new[] { "_north", "_south", "_east" })
                    Expect(ContentFinder<UnityEngine.Texture2D>.Get(path + dir, false) != null, "worn texture loads: " + path + dir);
            }
            Expect(lanceDef.graphic != null, "lance graphic resolves");
            Expect(tankDef.graphic != null, "tank graphic resolves");
            yield return 1;

            Pawn user = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault(x => !x.Dead && x.IsColonistPlayerControlled && !x.DevelopmentalStage.Baby());
            Expect(user != null, "a colonist to carry the gear");
            if (user == null) yield break;
            Map map = user.MapHeld;

            Mark("drain lance");
            Apparel lance = (Apparel)ThingMaker.MakeThing(lanceDef);
            CompApparelReloadable lanceCharges = lance.GetComp<CompApparelReloadable>();
            Expect(lanceCharges != null && lanceCharges.RemainingCharges == 1, "a new lance carries one charge");
            BS9000.CompTargetEffect_DrainLance drain = lance.GetComp<BS9000.CompTargetEffect_DrainLance>();
            Expect(drain != null, "lance carries the drain comp");
            if (lanceCharges == null || drain == null) yield break;
            user.apparel.Wear(lance, false);
            Expect(user.apparel.WornApparel.Contains(lance), "the colonist wears the lance");

            Pawn victim = SpawnTestSubject(map, user.Position);
            int packsBefore = Packs(map);
            int expectedPacks = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(6f * victim.BodySize));
            drain.DoEffectOn(user, victim);
            int packsAfter = Packs(map);
            Log.Message($"[PROBE] drain lance on {victim.LabelShort} (body size {victim.BodySize:F2}): dead {victim.Dead}, packs {packsBefore} -> {packsAfter}, expected +{expectedPacks}");
            Expect(victim.Dead, "the drained subject dies");
            Expect(packsAfter == packsBefore + expectedPacks, $"the drain yields {expectedPacks} hemogen packs");
            yield return 1;

            Mark("lance breaks");
            // The verb spends the charge after running the comps, so this is what the verb does next.
            lanceCharges.UsedOnce();
            Expect(lance.Destroyed, "the lance breaks once its charge is spent");
            Expect(!user.apparel.WornApparel.Contains(lance), "the broken lance leaves the colonist");
            yield return 1;

            Mark("tank storage");
            Apparel tank = (Apparel)ThingMaker.MakeThing(tankDef);
            CompApparelReloadable tankCharges = tank.GetComp<CompApparelReloadable>();
            BS9000.Comp_HemogenTank tankComp = tank.GetComp<BS9000.Comp_HemogenTank>();
            Expect(tankCharges != null && tankComp != null, "tank carries the reloadable and tank comps");
            if (tankCharges == null || tankComp == null) yield break;
            Expect(tankCharges.MaxCharges == 10, "the tank holds ten packs");
            Expect(tankCharges.RemainingCharges == tankCharges.MaxCharges, "a new tank starts full");
            Expect(tankCharges.AmmoDef == ThingDefOf.HemogenPack, "the tank is refilled with hemogen packs");
            user.apparel.Wear(tank, false);
            Expect(BS9000.HemogenTankUtility.WornTank(user) == tankComp, "a worn tank with charges is found on the wearer");
            yield return 1;

            Mark("tank treats blood loss");
            Pawn patient = SpawnTestSubject(map, user.Position);
            HealthUtility.AdjustSeverity(patient, HediffDefOf.BloodLoss, 0.4f);
            float lossBefore = BloodLoss(patient);
            int chargesBefore = tankCharges.RemainingCharges;
            bool treated = BS9000.HemogenTankUtility.Transfuse(tankComp, patient);
            Log.Message($"[PROBE] tank transfuse on {patient.LabelShort}: ok {treated}, charges {chargesBefore} -> {tankCharges.RemainingCharges}, blood loss {lossBefore:F3} -> {BloodLoss(patient):F3}");
            Expect(treated, "transfusing a bleeding patient succeeds");
            Expect(tankCharges.RemainingCharges == chargesBefore - 1, "the transfusion spends one charge");
            Expect(BloodLoss(patient) < lossBefore - 0.2f, "the transfusion cuts blood loss");
            yield return 1;

            Mark("tank leaves the healthy alone");
            Pawn healthy = SpawnTestSubject(map, user.Position);
            int chargesHeld = tankCharges.RemainingCharges;
            bool wasted = BS9000.HemogenTankUtility.Transfuse(tankComp, healthy);
            Expect(!wasted, "a patient who needs nothing is not transfused");
            Expect(tankCharges.RemainingCharges == chargesHeld, "no charge is spent on a patient who needs nothing");
            yield return 1;

            Mark("tank tops up hemogen");
            GeneDef hemogenic = DefDatabase<GeneDef>.GetNamedSilentFail("Hemogenic");
            Expect(hemogenic != null, "gene def Hemogenic exists");
            if (hemogenic != null && healthy.genes != null)
            {
                healthy.genes.AddGene(hemogenic, true);
                Gene_Hemogen gene = healthy.genes.GetFirstGeneOfType<Gene_Hemogen>();
                Expect(gene != null, "the test subject carries the hemogen gene");
                if (gene != null)
                {
                    gene.Value = 0f;
                    gene.SetTargetValuePct(0.5f);
                    Expect(BS9000.HemogenTankUtility.WantsHemogen(healthy), "an empty hemogenic pawn wants hemogen");
                    int chargesNow = tankCharges.RemainingCharges;
                    bool fed = BS9000.HemogenTankUtility.Transfuse(tankComp, healthy);
                    Log.Message($"[PROBE] tank hemogen on {healthy.LabelShort}: ok {fed}, charges {chargesNow} -> {tankCharges.RemainingCharges}, hemogen {gene.Value:F3}, target {gene.targetValue:F3}");
                    Expect(fed && tankCharges.RemainingCharges == chargesNow - 1, "the tank spends a charge on hemogen");
                    Expect(gene.Value > 0f, "the hemogen lands on the pawn");

                    // The threshold on the hemogen bar is what stops a pawn being topped up forever.
                    gene.Value = gene.Max;
                    Expect(!BS9000.HemogenTankUtility.WantsHemogen(healthy), "a pawn above the threshold wants nothing");
                    int chargesFull = tankCharges.RemainingCharges;
                    BS9000.HemogenTankUtility.Transfuse(tankComp, healthy);
                    Expect(tankCharges.RemainingCharges == chargesFull, "no charge is spent above the threshold");
                }
            }
            yield return 1;

            Mark("transfuse work giver");
            BS9000.WorkGiver_TankTransfuse giver = new BS9000.WorkGiver_TankTransfuse();
            Expect(!giver.ShouldSkip(user), "the giver runs for a pawn wearing a charged tank");
            Expect(!giver.HasJobOnThing(user, user), "the giver does not offer a job on the worker themself");

            // The case the giver exists for: downed, and bleeding past the treatment threshold.
            Pawn casualty = SpawnTestSubject(map, user.Position);
            HealthUtility.DamageUntilDowned(casualty, false);
            HealthUtility.AdjustSeverity(casualty, HediffDefOf.BloodLoss, 0.5f);
            bool offers = false;
            Job offered = null;
            Guard("work giver scan", () => offers = giver.HasJobOnThing(user, casualty, true));
            Guard("work giver job", () => offered = giver.JobOnThing(user, casualty, true));
            Log.Message($"[PROBE] transfuse giver on {casualty.LabelShort}: downed {casualty.Downed}, blood loss {BloodLoss(casualty):F3}, offered {offers}, job {(offered?.def?.defName ?? "none")}");
            Expect(offers, "the giver offers a job on a downed bleeding patient");
            Expect(offered != null && offered.def.defName == "BS9000_TankTransfuse", "the job is BS9000_TankTransfuse on that patient");
            Expect(offered == null || offered.targetA.Thing == casualty, "the job targets the patient");

            // An empty tank must stop the giver, or doctors queue work the tank cannot pay for.
            int held = tankCharges.RemainingCharges;
            for (int i = 0; i < held; i++) tankCharges.UsedOnce();
            Expect(tankCharges.RemainingCharges == 0, "the tank can be emptied");
            Expect(giver.ShouldSkip(user), "an empty tank stops the giver");
            Expect(!giver.HasJobOnThing(user, casualty, true), "an empty tank offers no job");
            yield return 1;

            Mark("gear cleanup");
            if (!tank.Destroyed)
            {
                if (user.apparel.WornApparel.Contains(tank)) user.apparel.Remove(tank);
                tank.Destroy();
            }
            foreach (Pawn p in new[] { victim, patient, healthy, casualty })
            {
                if (p != null && !p.Destroyed) p.Destroy();
            }
            yield return 1;
        }

        // ---- Landmark Editor: the tile gizmo and the editor window it opens ----

        // Draws the fulfillment need at the sizes the vanilla needs tab and the character editor use,
        // so the patched draw path is exercised inside a real OnGUI pass.
        class NeedDrawWindow : Verse.Window
        {
            public Need need;
            public int errors;
            public int frames;
            public string lastError;

            static readonly float[] widths = { 290f, 200f, 420f, 290f };
            static readonly float[] heights = { 60f, 60f, 70f, 40f };

            public NeedDrawWindow()
            {
                doCloseX = true;
                closeOnClickedOutside = false;
                draggable = true;
            }

            public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(480f, 320f);

            public override void DoWindowContents(UnityEngine.Rect inRect)
            {
                frames++;
                float y = 0f;
                for (int i = 0; i < widths.Length; i++)
                {
                    try
                    {
                        need.DrawOnGUI(new UnityEngine.Rect(inRect.x, inRect.y + y, widths[i], heights[i]),
                            int.MaxValue, -1f, true, true, null, true);
                    }
                    catch (Exception e)
                    {
                        errors++;
                        lastError = e.GetType().Name + ": " + First(e.Message);
                    }
                    y += heights[i] + 6f;
                }
            }
        }

        static IEnumerable<int> AspirationTest()
        {
            foreach (int x in AspirationRun()) yield return x;
            Log.Message($"[PROBE] aspiration failures: {autoFails}");
        }

        static string AspirationState(object need)
        {
            var list = (System.Collections.IList)AccessTools.Field(need.GetType(), "Aspirations").GetValue(need);
            var ticks = (System.Collections.IList)AccessTools.Field(need.GetType(), "completedTicks").GetValue(need);
            var parts = new List<string>();
            for (int i = 0; i < list.Count; i++) parts.Add(((Def)list[i]).defName + "~" + (i < ticks.Count ? ticks[i] : (object)"?"));
            return string.Join(",", parts.ToArray());
        }

        static IEnumerable<int> AspirationRun()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Type patch = AccessTools.TypeByName("AspirationControl.Need_Fulfillment_DrawOnGUI_Patch");
            Expect(patch != null, "Aspiration Control assembly is loaded");
            if (patch == null) yield break;

            // 1. The split of the need's rect. Every combination must stay inside the rect it was given,
            // which is the whole point of the patch: the stock strip hangs below it.
            Mark("layout");
            MethodInfo layout = AccessTools.Method(patch, "Layout");
            Expect(layout != null, "the patch exposes Layout");
            if (layout == null) yield break;
            float minBar = (float)AccessTools.Field(patch, "MinBarHeight").GetValue(null);
            int cases = 0, outside = 0, mismatched = 0, thinBar = 0, fitted = 0;
            string firstBad = null;
            foreach (float w in new float[] { 90f, 120f, 180f, 240f, 290f, 420f, 600f })
                foreach (float h in new float[] { 20f, 30f, 40f, 54f, 60f, 70f, 90f, 120f })
                    foreach (int n in new int[] { 0, 1, 2, 3, 4, 5 })
                        foreach (float m in new float[] { -1f, 0f, 10f, 29f })
                        {
                            cases++;
                            var rect = new UnityEngine.Rect(17f, 23f, w, h);
                            var args = new object[] { rect, m, n, null, null };
                            bool ok = (bool)layout.Invoke(null, args);
                            var bar = (UnityEngine.Rect)args[3];
                            var strip = (UnityEngine.Rect)args[4];
                            if (!ok)
                            {
                                if (bar != rect) { mismatched++; firstBad = firstBad ?? ("no-split keeps the rect w" + w + " h" + h + " n" + n + " m" + m); }
                                continue;
                            }
                            fitted++;
                            bool inside = strip.xMin >= rect.xMin - 0.01f && strip.xMax <= rect.xMax + 0.01f
                                && strip.yMin >= rect.yMin - 0.01f && strip.yMax <= rect.yMax + 0.01f;
                            if (!inside) { outside++; firstBad = firstBad ?? ("strip " + strip + " outside " + rect + " n" + n + " m" + m); }
                            if (UnityEngine.Mathf.Abs(bar.height + strip.height - rect.height) > 0.01f || bar.yMin != rect.yMin || bar.width != rect.width)
                            { mismatched++; firstBad = firstBad ?? ("bar " + bar + " does not fill the rest of " + rect + " n" + n + " m" + m); }
                            if (bar.height < minBar - 0.01f) { thinBar++; firstBad = firstBad ?? ("bar height " + bar.height + " under the floor, w" + w + " h" + h + " n" + n + " m" + m); }
                        }
            Log.Message("[PROBE] aspiration layout cases " + cases + ", split " + fitted + ", bar-only " + (cases - fitted));
            Expect(outside == 0, "the icon strip stays inside the need's rect in every case (" + outside + " of " + cases + " outside)");
            Expect(mismatched == 0, "the bar takes exactly the rest of the rect (" + mismatched + " off)");
            Expect(thinBar == 0, "the bar never drops under its floor of " + minBar + "px (" + thinBar + " too thin)");
            Expect(fitted > 0, "a strip fits in the sizes the needs lists use");
            if (firstBad != null) Log.Message("[PROBE] aspiration layout first bad case: " + firstBad);

            // 2. A live pawn with the need.
            Mark("pawn");
            NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail("VAspirE_Fulfillment");
            Expect(needDef != null, "the fulfillment need def is loaded");
            if (needDef == null || Find.CurrentMap == null) yield break;
            Pawn pawn = null;
            object need = null;
            foreach (Pawn p in Find.CurrentMap.mapPawns.FreeColonists)
            {
                Need n = p.needs?.TryGetNeed(needDef);
                if (n != null) { pawn = p; need = n; break; }
            }
            Expect(pawn != null, "a colonist on the map has the fulfillment need");
            if (pawn == null) yield break;
            var aspirations = (System.Collections.IList)AccessTools.Field(need.GetType(), "Aspirations").GetValue(need);
            var ticks = (System.Collections.IList)AccessTools.Field(need.GetType(), "completedTicks").GetValue(need);
            Log.Message("[PROBE] aspiration pawn " + pawn.LabelShort + ": " + AspirationState(need));
            string original = AspirationState(need);
            Expect(aspirations.Count > 0 && ticks.Count == aspirations.Count, "the pawn has aspirations and one tick per aspiration");

            // 3. Picking an aspiration for a slot.
            Mark("picker");
            Type aspDefType = AccessTools.TypeByName("VAspirE.AspirationDef");
            var allDefs = GenDefDatabase.GetAllDefsInDatabaseForDef(aspDefType).Cast<Def>().ToList();
            Expect(allDefs.Count > 0, "the aspiration defs are loaded (" + allDefs.Count + ")");
            Def picked = allDefs.FirstOrDefault(d => !aspirations.Cast<Def>().Contains(d));
            if (picked == null) yield break;
            int slots = aspirations.Count;
            Guard("set aspiration", () => AccessTools.Method(patch, "SetAt").Invoke(null, new object[] { need, 0, picked }));
            Expect(aspirations.Count == slots && ReferenceEquals(aspirations[0], picked), "picking replaces slot 0 with " + picked.defName + " and adds no slot");
            Expect(((Need)need).CurLevel == ticks.Cast<int>().Count(t => t != -1), "the need level matches the completed slots after picking");

            Guard("remove aspiration", () => AccessTools.Method(patch, "RemoveAt").Invoke(null, new object[] { need, 0 }));
            Expect(aspirations.Count == slots - 1 && ticks.Count == slots - 1, "removing a slot drops its aspiration and its tick");

            Guard("add aspiration back", () => AccessTools.Method(patch, "SetAt").Invoke(null, new object[] { need, aspirations.Count, picked }));
            Expect(aspirations.Count == slots && ReferenceEquals(aspirations[slots - 1], picked), "the empty slot at the end adds an aspiration");

            // 4. The Character Editor preset round trip.
            Mark("preset round trip");
            Type ext = AccessTools.TypeByName("CharacterEditor.MindTool");
            MethodInfo read = ext == null ? null : AccessTools.Method(ext, "GetAllNeedsAsSeparatedString");
            MethodInfo write = ext == null ? null : AccessTools.Method(ext, "SetNeeds");
            Expect(read != null && write != null, "Character Editor's needs preset methods are there to patch");
            if (read == null || write == null) yield break;
            string saved = (string)read.Invoke(null, new object[] { pawn });
            Expect(saved != null && saved.Contains("|ASPIRATIONS|"), "the preset string carries the aspirations");
            string savedState = AspirationState(need);

            // Scramble, then load the preset back.
            Def other = allDefs.FirstOrDefault(d => !aspirations.Cast<Def>().Contains(d));
            if (other != null) Guard("scramble", () => AccessTools.Method(patch, "SetAt").Invoke(null, new object[] { need, 1, other }));
            Guard("drop a slot", () => AccessTools.Method(patch, "RemoveAt").Invoke(null, new object[] { need, 0 }));
            Expect(AspirationState(need) != savedState, "the pawn's aspirations really changed before the reload");
            Guard("load preset", () => write.Invoke(null, new object[] { pawn, saved }));
            Expect(AspirationState(need) == savedState, "loading the preset restores the same aspirations and progress"
                + (AspirationState(need) == savedState ? "" : ": " + AspirationState(need) + " not " + savedState));

            // A preset written before this mod has no aspiration entry and must still load.
            string legacy = string.Join(":", saved.Split(':').Where(e => !e.Contains("|ASPIRATIONS|")).ToArray());
            string beforeLegacy = AspirationState(need);
            Guard("load preset without aspirations", () => write.Invoke(null, new object[] { pawn, legacy }));
            Expect(AspirationState(need) == beforeLegacy, "a preset saved before this mod loads and leaves the aspirations alone");

            // 5. The need draws through the real OnGUI path, at the rect sizes the needs lists use.
            Mark("draw the need");
            var win = new NeedDrawWindow();
            win.need = (Need)need;
            Guard("open the draw window", () => Find.WindowStack.Add(win));
            for (int i = 0; i < 40; i++) yield return 1;
            Expect(win.frames > 0, "the draw window ran OnGUI (" + win.frames + " frames)");
            Expect(win.errors == 0, "the fulfillment need draws without throwing at any size ("
                + win.errors + (win.lastError == null ? "" : ", " + win.lastError) + ")");
            Shot("aspiration-need");
            for (int i = 0; i < 10; i++) yield return 1;
            Guard("close the draw window", () => win.Close(false));

            // The load warns that English translation data has errors but never says which; write the list out.
            Mark("translation report");
            Guard("translation report", () => LanguageReportGenerator.SaveTranslationReport());
            Log.Message("[PROBE] translation report in " + GenFilePaths.SaveDataFolderPath);

            Mark("restore");
            Log.Message("[PROBE] aspiration state at the end: " + AspirationState(need) + " (started as " + original + ")");
        }

        static IEnumerable<int> LandmarkTest()
        {
            foreach (int x in LandmarkRun()) yield return x;
            Log.Message($"[PROBE] landmark failures: {autoFails}");
        }

        static bool DeterministicLandmark(LandmarkDef def) =>
            def.mutatorChances != null && def.mutatorChances.Any(mc => mc != null && mc.required && mc.chance >= 1f && mc.mutator != null);

        // Matched by label only, so the mod's assembly is not referenced.
        static List<Command_Action> TileActions(RimWorld.Planet.Tile t) =>
            (t.GetGizmos() ?? Enumerable.Empty<Gizmo>()).OfType<Command_Action>().ToList();

        static Window LandmarkDialog() =>
            Find.WindowStack.Windows.FirstOrDefault(w => w.GetType().Name == "Dialog_LandmarkEditor");

        static IEnumerable<MutatorChance> AllChances(LandmarkDef def)
        {
            if (def.mutatorChances != null) foreach (MutatorChance mc in def.mutatorChances) yield return mc;
            if (def.comboLandmarkMutators != null) foreach (MutatorChance mc in def.comboLandmarkMutators) yield return mc;
        }

        static IEnumerable<int> LandmarkRun()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Expect(ModsConfig.OdysseyActive, "landmark test needs Odyssey");
            if (!ModsConfig.OdysseyActive) yield break;
            Expect(Find.World != null && Find.CurrentMap != null, "a world and a map exist (quick-test play)");
            if (Find.World == null || Find.CurrentMap == null) yield break;

            var pt = Find.CurrentMap.Tile;
            var t = Find.World.grid[pt];
            Expect(t != null, "world grid has a tile for the current map");
            if (t == null) yield break;
            LandmarkDef original = t.Landmark?.def;
            string originalName = original == null ? "none" : original.defName;
            Log.Message($"[PROBE] landmark tile {pt}: current landmark {originalName}");

            Mark("gizmos");
            List<Command_Action> acts = TileActions(t);
            Command_Action editGizmo = acts.FirstOrDefault(a => a.defaultLabel == "Edit tile");
            Expect(editGizmo != null, "Edit tile gizmo present (Harmony postfix applied)");
            if (editGizmo == null) yield break;
            int editGizmos = acts.Count(a => a.defaultLabel == "Edit tile");
            int oldGizmos = acts.Count(a => a.defaultLabel == "Set landmark" || a.defaultLabel == "Remove landmark" || a.defaultLabel == "Edit landmark");
            Expect(editGizmos == 1 && oldGizmos == 0, "exactly one Edit tile gizmo and none of the older ones (" + editGizmos + " and " + oldGizmos + ")");

            Mark("open the editor");
            Guard("edit landmark gizmo", () => editGizmo.action());
            Window dlg = null;
            foreach (int x in WaitFor(() => (dlg = LandmarkDialog()) != null, 120)) yield return x;
            Expect(dlg != null, "the gizmo opens the landmark editor window");
            if (dlg == null) yield break;
            Type dt = dlg.GetType();

            Mark("the window draws");
            for (int i = 0; i < 30; i++) yield return 1;
            Expect(Find.WindowStack.Windows.Contains(dlg), "the editor survives 30 frames of drawing");

            Mark("list contents");
            List<LandmarkDef> defs = DefDatabase<LandmarkDef>.AllDefsListForReading;
            var rows = (System.Collections.IList)AccessTools.Field(dt, "landmarkRows").GetValue(dlg);
            Expect(rows != null && rows.Count == defs.Count, "the editor lists every landmark def (" + (rows == null ? 0 : rows.Count) + " of " + defs.Count + ")");
            if (rows == null || rows.Count == 0) yield break;
            Type rowType = rows[0].GetType();
            FieldInfo fDef = AccessTools.Field(rowType, "def"), fSub = AccessTools.Field(rowType, "sub"), fValid = AccessTools.Field(rowType, "valid");
            FieldInfo fActive = AccessTools.Field(rowType, "active"), fBlocked = AccessTools.Field(rowType, "blocked");
            Expect(fDef != null && fSub != null && fValid != null && fActive != null && fBlocked != null, "editor rows expose def, sub, valid, active and blocked");
            if (fDef == null || fSub == null || fValid == null || fActive == null || fBlocked == null) yield break;

            // Several defs share one label ("lake" covers four), so each row must also carry its defName.
            var untagged = new List<string>();
            foreach (object r in rows)
            {
                var d = (LandmarkDef)fDef.GetValue(r);
                var sub = (string)fSub.GetValue(r);
                if (sub == null || sub.IndexOf(d.defName, StringComparison.Ordinal) < 0) untagged.Add(d.defName);
            }
            Expect(untagged.Count == 0, "every row names its defName, so landmarks sharing a label are tellable apart" + (untagged.Count == 0 ? "" : ": missing on " + string.Join(", ", untagged.Take(5))));

            Mark("clean slate");
            if (t.Landmark != null)
            {
                Guard("remove existing landmark", () => AccessTools.Method(dt, "RemoveLandmark").Invoke(dlg, null));
                Expect(t.Landmark == null, "existing landmark removed before the test");
            }

            Mark("pick def");
            LandmarkDef chosen = defs.FirstOrDefault(d => d.defName == "Valley" && DeterministicLandmark(d)) ?? defs.FirstOrDefault(DeterministicLandmark);
            if (chosen == null) { Expect(false, "no deterministic landmark def found"); yield break; }
            Log.Message("[PROBE] landmark chosen " + chosen.defName);
            var required = chosen.mutatorChances.Where(mc => mc != null && mc.required && mc.chance >= 1f && mc.mutator != null).Select(mc => mc.mutator).ToList();
            var before = t.Mutators == null ? new List<TileMutatorDef>() : t.Mutators.ToList();

            // The count on the bare tile. Without it a later zero cannot be told apart from a tile
            // where nothing fits in the first place.
            int validBefore = 0;
            foreach (object r in rows) if ((bool)fValid.GetValue(r)) validBefore++;
            Log.Message("[PROBE] landmark rows that fit the bare tile: " + validBefore + " of " + rows.Count
                + "; hilliness " + t.hilliness + ", biome " + (t.PrimaryBiome != null ? t.PrimaryBiome.defName : "none")
                + ", mutators " + (before.Count == 0 ? "none" : string.Join(", ", before.Select(m => m.defName))));

            Mark("set through the window");
            Guard("set landmark", () => AccessTools.Method(dt, "SetLandmark").Invoke(dlg, new object[] { chosen }));
            yield return 1;

            Mark("set checks");
            Expect(t.Landmark != null && t.Landmark.def == chosen, "Set makes " + chosen.defName + " the tile's landmark");
            Expect(t.Landmark != null && !string.IsNullOrEmpty(t.Landmark.name), "new landmark has a name");
            Expect(t.Landmark != null && ReferenceEquals(Find.World.landmarks[pt], t.Landmark), "world landmark table holds the same landmark object");
            Expect(t.Mutators != null && required.All(m => t.Mutators.Contains(m)), "required mutators of " + chosen.defName + " are on the tile: " + string.Join(", ", required.Select(m => m.defName)));
            var added = AllChances(chosen).Where(mc => mc != null && mc.mutator != null && !before.Contains(mc.mutator) && t.Mutators != null && t.Mutators.Contains(mc.mutator)).Select(mc => mc.mutator).Distinct().ToList();
            Log.Message("[PROBE] landmark added mutators: " + (added.Count == 0 ? "none" : string.Join(", ", added.Select(m => m.defName))));

            Mark("validity is judged on an empty tile");
            var after = (System.Collections.IList)AccessTools.Field(dt, "landmarkRows").GetValue(dlg);
            int validRows = 0;
            foreach (object r in after) if ((bool)fValid.GetValue(r)) validRows++;
            // Compared against the bare tile, not against zero. A quick-test tile can legitimately fit no
            // landmark at all - a Mountainous Tundra tile carrying Mountain and Caves blocks every mountain
            // and cave landmark on shared categories and fits none of the rest - and then "more than none"
            // fails whatever the editor does. What the lift-out owes is that a landmark on the tile changes
            // nothing: the same defs fit with it as without it.
            Expect(validRows == validBefore, "a landmark on the tile does not change which landmarks fit ("
                + validRows + " with it, " + validBefore + " on the bare tile)");
            Expect(t.Landmark != null && t.Landmark.def == chosen, "the validity pass puts the tile's landmark back");

            Mark("remove through the window");
            Guard("remove landmark", () => AccessTools.Method(dt, "RemoveLandmark").Invoke(dlg, null));
            yield return 1;
            Expect(t.Landmark == null, "Remove clears the tile's landmark");
            Expect(Find.World.landmarks[pt] == null, "world landmark table has no entry for the tile");
            var left = added.Where(m => t.Mutators != null && t.Mutators.Contains(m)).Select(m => m.defName).ToList();
            Expect(left.Count == 0, "Remove strips every mutator the landmark added" + (left.Count == 0 ? "" : ": still on the tile " + string.Join(", ", left)));

            Mark("features tab");
            List<TileMutatorDef> mutDefs = DefDatabase<TileMutatorDef>.AllDefsListForReading;
            var feats = (System.Collections.IList)AccessTools.Field(dt, "featureRows").GetValue(dlg);
            Expect(feats != null && feats.Count == mutDefs.Count, "the editor lists every tile feature (" + (feats == null ? 0 : feats.Count) + " of " + mutDefs.Count + ")");
            if (feats == null || feats.Count == 0) yield break;

            bool orderOk = true, seenInactive = false;
            foreach (object r in feats)
            {
                bool active = (bool)fActive.GetValue(r);
                if (!active) seenInactive = true;
                else if (seenInactive) orderOk = false;
            }
            Expect(orderOk, "the tile's own features are listed before the rest");

            Mark("remove a feature the tile already had");
            TileMutatorDef owned = t.Mutators == null ? null : t.Mutators.FirstOrDefault();
            if (owned == null) Log.Message("[PROBE] landmark: tile has no world-generated feature to remove, step skipped");
            else
            {
                Guard("remove owned feature", () => AccessTools.Method(dt, "ToggleFeature").Invoke(dlg, new object[] { owned }));
                yield return 1;
                Expect(t.Mutators == null || !t.Mutators.Contains(owned), "Remove takes the world-generated feature " + owned.defName + " off the tile");
                Guard("restore owned feature", () => AccessTools.Method(dt, "ToggleFeature").Invoke(dlg, new object[] { owned }));
                yield return 1;
                Expect(t.Mutators != null && t.Mutators.Contains(owned), "Add puts " + owned.defName + " back on the tile");
            }

            Mark("add and remove a feature the tile lacks");
            var addable = new List<TileMutatorDef>();
            foreach (object r in (System.Collections.IList)AccessTools.Field(dt, "featureRows").GetValue(dlg))
                if (!(bool)fActive.GetValue(r) && !(bool)fBlocked.GetValue(r) && (bool)fValid.GetValue(r)) addable.Add((TileMutatorDef)fDef.GetValue(r));
            // Prefer a modded feature, since those are the ones the editor used to miss.
            TileMutatorDef feature = addable.FirstOrDefault(d => d.defName == "VEE_DeadlifeVents")
                ?? addable.FirstOrDefault(d => d.modContentPack != null && !d.modContentPack.IsCoreMod && !d.modContentPack.IsOfficialMod)
                ?? addable.FirstOrDefault();
            if (feature == null) Expect(false, "the editor offers at least one feature this tile can take");
            else
            {
                Log.Message("[PROBE] landmark feature chosen " + feature.defName + " from " + (feature.modContentPack == null ? "unknown" : feature.modContentPack.Name));
                Guard("add feature", () => AccessTools.Method(dt, "ToggleFeature").Invoke(dlg, new object[] { feature }));
                yield return 1;
                Expect(t.Mutators != null && t.Mutators.Contains(feature), "Add puts " + feature.defName + " on the tile");

                var featRow = ((System.Collections.IList)AccessTools.Field(dt, "featureRows").GetValue(dlg)).Cast<object>()
                    .FirstOrDefault(r => ReferenceEquals(fDef.GetValue(r), feature));
                Expect(featRow != null && (bool)fActive.GetValue(featRow), "the row for " + feature.defName + " now reads as on the tile");

                Guard("remove feature", () => AccessTools.Method(dt, "ToggleFeature").Invoke(dlg, new object[] { feature }));
                yield return 1;
                Expect(t.Mutators == null || !t.Mutators.Contains(feature), "Remove takes " + feature.defName + " off the tile");
            }
            Mark("close");
            Guard("close the editor", () => dlg.Close(false));
            yield return 1;
            Expect(LandmarkDialog() == null, "the editor closes");

            Mark("restore");
            if (original != null) Guard("restore landmark", () => Find.World.landmarks.AddLandmark(original, pt, t.Layer, true));
            string restored = original == null ? "no landmark to restore" : t.Landmark?.def == original ? "restored " + originalName : "not restored";
            Log.Message("[PROBE] landmark restore: " + restored);
        }

        // ---- Select Starting Site: does the Edit tile gizmo reach the world gizmo grid on that page ----

        static IEnumerator<int> sitePageRun;
        static bool sitePageGizmoDrawn;
        static int sitePageDraws, sitePageGizmos;
        static string sitePageLabels = "";

        // Runs from the main menu, like the static and dump modes, because the new game is started from there.
        static void SitePageFrame()
        {
            if (sitePageRun == null) { Log.Message("[PROBE] sitepage start"); sitePageRun = SitePageTest().GetEnumerator(); }
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                while (sw.ElapsedMilliseconds < 80)
                {
                    if (!sitePageRun.MoveNext()) { Finish(); sitePageRun = Idle().GetEnumerator(); return; }
                    if (sitePageRun.Current > 0) return;
                }
            }
            catch (Exception e) { Log.Error("[PROBE] step threw: " + e); sitePageRun = Idle().GetEnumerator(); }
        }

        static IEnumerable<int> SitePageTest()
        {
            foreach (int x in SitePageRun()) yield return x;
            Log.Message($"[PROBE] sitepage failures: {autoFails}");
        }

        static T SetupField<T>(Page_CreateWorldParams page, string name) => (T)AccessTools.Field(typeof(Page_CreateWorldParams), name).GetValue(page);

        // The same call CanDoNext queues for the world parameters page, then the page that CanDoNext opens after it.
        static void GenerateStartingWorld(float coverage, string seed, RimWorld.Planet.OverallRainfall rainfall, RimWorld.Planet.OverallTemperature temperature, RimWorld.Planet.OverallPopulation population, RimWorld.Planet.LandmarkDensity landmarkDensity, List<FactionDef> factions, float pollution)
        {
            Find.GameInitData.ResetWorldRelatedMapInitData();
            Current.Game.World = RimWorld.Planet.WorldGenerator.GenerateWorld(coverage, seed, rainfall, temperature, population, landmarkDensity, factions, pollution);
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Find.World.renderer.RegenerateAllLayersNow();
                Find.WindowStack.Add(new Page_SelectStartingSite());
            });
        }

        // Prefix on DrawGizmoGridFor: records what the gizmo grid was handed, without changing it.
        static void SitePageGridPre(IEnumerable<object> __0)
        {
            var objects = __0 == null ? new List<object>() : __0.ToList();
            var commands = objects.OfType<Command_Action>().ToList();
            sitePageDraws++;
            sitePageGizmos = objects.OfType<Gizmo>().Count();
            sitePageLabels = string.Join(", ", commands.Select(c => c.defaultLabel));
            if (commands.Any(c => c.defaultLabel == "Edit tile")) sitePageGizmoDrawn = true;
        }

        static IEnumerable<int> SitePageRun()
        {
            foreach (int x in Frames(600)) yield return x; // the main menu settles, as in MenuFrame
            Log.Message("[PROBE] sitepage odyssey active: " + ModsConfig.OdysseyActive);

            Mark("new game");
            Expect(ScenarioDefOf.Crashlanded != null, "Crashlanded scenario def exists");
            if (ScenarioDefOf.Crashlanded == null) yield break;
            Guard("new game", () =>
            {
                Game.ClearCaches();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.Scenario = ScenarioDefOf.Crashlanded.scenario;
                Current.Game.Scenario.PreConfigure();
                Find.GameInitData.startedFromEntry = true;
            });

            Mark("world parameters");
            var setup = new Page_CreateWorldParams();
            setup.Reset();
            const float coverage = 0.05f; // Reset() picks 0.3 or 0.5 outside dev mode; the probe asks for a small world instead.
            string seed = SetupField<string>(setup, "seedString");
            RimWorld.Planet.OverallRainfall rainfall = SetupField<RimWorld.Planet.OverallRainfall>(setup, "rainfall");
            RimWorld.Planet.OverallTemperature temperature = SetupField<RimWorld.Planet.OverallTemperature>(setup, "temperature");
            RimWorld.Planet.OverallPopulation population = SetupField<RimWorld.Planet.OverallPopulation>(setup, "population");
            RimWorld.Planet.LandmarkDensity landmarkDensity = SetupField<RimWorld.Planet.LandmarkDensity>(setup, "landmarkDensity");
            List<FactionDef> factions = SetupField<List<FactionDef>>(setup, "factions");
            Expect(factions != null, "world parameters page has a faction list");
            if (factions == null) yield break;

            Mark("generate the world");
            Guard("queue world generation", () => LongEventHandler.QueueLongEvent(() => GenerateStartingWorld(coverage, seed, rainfall, temperature, population, landmarkDensity, factions, setup.pollution), "GeneratingWorld", true, null, true, false, null));
            foreach (int x in WaitFor(() => Find.World != null && !LongEventHandler.AnyEventNowOrWaiting, 9000)) yield return x;
            Expect(Find.World != null && !LongEventHandler.AnyEventNowOrWaiting, "world generated within 9000 frames");
            if (Find.World == null || LongEventHandler.AnyEventNowOrWaiting) yield break;

            Mark("open the page");
            foreach (int x in WaitFor(() => Find.WindowStack.Windows.OfType<Page_SelectStartingSite>().Any(), 600)) yield return x;
            Page_SelectStartingSite page = Find.WindowStack.Windows.OfType<Page_SelectStartingSite>().FirstOrDefault();
            Expect(page != null, "Select Starting Site page opened");
            if (page == null) yield break;
            foreach (int x in WaitFor(() => RimWorld.Planet.WorldRendererUtility.WorldSelected, 600)) yield return x;
            foreach (int x in Frames(10)) yield return x; // let the UI settle before the checks and the screenshot

            Mark("checks");
            Window top = Find.WindowStack.Windows.FirstOrDefault();
            Expect(ReferenceEquals(top, page), "the page is the top window (top: " + (top == null ? "none" : top.GetType().Name) + ")");
            Expect(RimWorld.Planet.WorldRendererUtility.WorldSelected, "world view is selected (render mode " + RimWorld.Planet.WorldRendererUtility.CurrentWorldRenderMode + ")");
            Expect(Find.TilePicker != null && !Find.TilePicker.Active, "tile picker is not active");

            RimWorld.Planet.PlanetTile selected = Find.WorldSelector.SelectedTile;
            if (selected == RimWorld.Planet.PlanetTile.Invalid)
            {
                Log.Message("[PROBE] sitepage selected tile was invalid, setting the starting tile " + Find.GameInitData.startingTile);
                Find.WorldSelector.SelectedTile = Find.GameInitData.startingTile;
                selected = Find.WorldSelector.SelectedTile;
            }
            Expect(selected != RimWorld.Planet.PlanetTile.Invalid, "a world tile is selected (" + selected + ")");
            if (selected == RimWorld.Planet.PlanetTile.Invalid || selected.Tile == null) { Expect(false, "the selected tile has a world tile to read gizmos from"); yield break; }

            List<Command_Action> tileActions = TileActions(selected.Tile);
            Expect(tileActions.Any(a => a.defaultLabel == "Edit tile"), "the selected tile's gizmos include Edit tile (" + tileActions.Count + " actions)");

            Mark("screenshot");
            Shot("sitepage");
            foreach (int x in Frames(10)) yield return x;
            string shotPath = Path.Combine(GenFilePaths.ScreenshotFolderPath, "LP17Probe-sitepage.png");
            Expect(File.Exists(shotPath), "screenshot written: " + shotPath);

            Mark("grid drawer");
            sitePageDraws = 0;
            sitePageGizmoDrawn = false;
            Harmony sitePatch = new Harmony("local.lp17.probe.sitepage");
            MethodInfo drawGrid = AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGridFor));
            Guard("patch DrawGizmoGridFor", () => sitePatch.Patch(drawGrid, prefix: new HarmonyMethod(typeof(Probe), nameof(SitePageGridPre))));
            foreach (int x in Frames(60)) yield return x;
            Guard("unpatch DrawGizmoGridFor", () => sitePatch.Unpatch(drawGrid, HarmonyPatchType.Prefix, "local.lp17.probe.sitepage"));
            Expect(sitePageDraws > 0, "the world gizmo grid was drawn on the page (" + sitePageDraws + " draws)");
            Expect(sitePageGizmoDrawn, "Edit tile reached DrawGizmoGridFor on the page");
            Log.Message($"[PROBE] sitepage grid: last draw had {sitePageGizmos} gizmos, Command_Action labels: {sitePageLabels}");
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
