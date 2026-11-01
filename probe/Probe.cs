// Test-only build of LP17 Hotfix (dotnet build -p:Probe=true). Env LP17_PROBE selects the scan:
//   static : JIT every active mod method, check Harmony targets, dump the patch table to LP17_PROBE_OUT.
//   dump   : write every faction, pawn kind, weapon and apparel def to LP17_PROBE_OUT.<table>.tsv.
//   gear   : load LP17_PROBE_SAVE, generate every faction's pawn kinds and visitor group, write what they wear and carry to LP17_PROBE_OUT.gear.tsv.
//   soak   : load LP17_PROBE_SAVE, autosave off, open UI tabs, build every def, generate pawn kinds, spawn things,
//            fire incidents and quests, then run ticks. The game is killed afterwards and never saved.
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
            if (mode == "static" || mode == "dump") new Harmony("local.lp17.probe").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Probe), nameof(MenuFrame)));
            else if (mode == "soak" || mode == "gear")
            {
                Harmony h = new Harmony("local.lp17.probe");
                h.Patch(AccessTools.Method(typeof(Autosaver), "AutosaverTick"), prefix: new HarmonyMethod(typeof(Probe), nameof(No)));
                h.Patch(AccessTools.Method(typeof(Autosaver), "DoAutosave"), prefix: new HarmonyMethod(typeof(Probe), nameof(No)));
                h.Patch(AccessTools.Method(typeof(TickManager), "TickManagerUpdate"), postfix: new HarmonyMethod(typeof(Probe), nameof(Frame)));
                LongEventHandler.ExecuteWhenFinished(() => { Log.Message("[PROBE] loading " + save); GameDataSaveLoader.LoadGame(save); });
            }
        }

        static bool No() => false;

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
            Log.Message("[PROBE] done");
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
            Log.Message("[PROBE] done");
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
            if (run == null) { Log.Message("[PROBE] playing"); run = (mode == "gear" ? Gear() : Soak()).GetEnumerator(); }
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                while (sw.ElapsedMilliseconds < 80)
                {
                    if (!run.MoveNext()) { Log.Message("[PROBE] done"); run = Idle().GetEnumerator(); return; }
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
                    _ = t.GetGizmos().ToList();
                    _ = t.GetInspectTabs()?.ToList();
                });
                yield return 0;
            }
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
