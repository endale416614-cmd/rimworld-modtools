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
            AppDomain.CurrentDomain.FirstChanceException += (s, e) => CountException(e.Exception);
            if (mode == "static" || mode == "dump") new Harmony("local.lp17.probe").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Probe), nameof(MenuFrame)));
            else if (mode == "soak" || mode == "gear" || mode == "scan" || mode == "autoresearch")
            {
                Harmony h = new Harmony("local.lp17.probe");
                h.Patch(AccessTools.Method(typeof(Autosaver), "AutosaverTick"), prefix: new HarmonyMethod(typeof(Probe), nameof(No)));
                h.Patch(AccessTools.Method(typeof(Autosaver), "DoAutosave"), prefix: new HarmonyMethod(typeof(Probe), nameof(No)));
                h.Patch(AccessTools.Method(typeof(TickManager), "TickManagerUpdate"), postfix: new HarmonyMethod(typeof(Probe), nameof(Frame)));
                LongEventHandler.ExecuteWhenFinished(() => { Log.Message("[PROBE] loading " + save); GameDataSaveLoader.LoadGame(save); });
            }
        }

        static bool No() => false;

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
                if (w.forcePause || w is Dialog_MessageBox) w.Close(false);
            if (run == null) { Log.Message("[PROBE] playing"); run = (mode == "gear" ? Gear() : mode == "scan" ? Scan() : mode == "autoresearch" ? AutoResearchTest() : Soak()).GetEnumerator(); }
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
