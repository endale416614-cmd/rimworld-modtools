// patch17: IL patches that make specific RimWorld 1.6 mod DLLs bind to the 1.7 (Waylith/.NET 10) API.
// Each rule matches one broken reference found by refcheck and rewrites only that instruction.
// Branches that targeted a rewritten instruction still land on the start of its replacement.
// Usage: patch17 IN.dll OUT.dll [--ref DIR]...   (--ref: where referenced assemblies live, e.g. the game folder;
// Cecil resolves them only to write constants/enums. Prints one line per rule with its hit count; exit 1 if nothing changed)
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

if (args.Length < 2) { Console.Error.WriteLine("usage: patch17 IN.dll OUT.dll [--ref DIR]..."); return 2; }
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
for (int i = 2; i + 1 < args.Length; i += 2) if (args[i] == "--ref") resolver.AddSearchDirectory(args[i + 1]);
// 1.7 has no UnityEngine.*Module assemblies; the game binds them to UnityStubs.dll. Do the same when Cecil needs a type.
resolver.ResolveFailure += (_, n) => n.Name.StartsWith("UnityEngine") && n.Name != "UnityStubs"
    ? resolver.Resolve(new AssemblyNameReference("UnityStubs", new Version(1, 0, 0, 0))) : null;
var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { ReadSymbols = false, AssemblyResolver = resolver });
var ts = module.TypeSystem;
var hits = new Dictionary<string, int>();
void Hit(string rule) => hits[rule] = hits.GetValueOrDefault(rule) + 1;

// Replace Stuff 1.6+ dropped NewThingDefOf; give the compat DLL its own [DefOf] class for the same vanilla defs.
TypeDefinition defOf = null;
FieldReference DefOfField(FieldReference old)
{
    if (defOf == null)
    {
        var verse = module.AssemblyReferences.First(a => a.Name == "Assembly-CSharp");
        var thingDef = new TypeReference("Verse", "ThingDef", module, verse);
        var attr = new TypeReference("RimWorld", "DefOf", module, verse);
        defOf = new TypeDefinition("Patch17", "NewThingDefOf17", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class, ts.Object);
        defOf.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor", ts.Void, attr) { HasThis = true }));
        foreach (var n in new[] { "HandTailoringBench", "ElectricTailoringBench", "FueledStove", "ElectricStove" })
            defOf.Fields.Add(new FieldDefinition(n, FieldAttributes.Public | FieldAttributes.Static, thingDef));
        module.Types.Add(defOf);
    }
    var f = defOf.Fields.FirstOrDefault(x => x.Name == old.Name);
    if (f == null) throw new Exception("no replacement for NewThingDefOf::" + old.Name);
    return f;
}

// Graphics Settings+: its ModContentLoader.LoadTexture prefix always returns false. On 1.7 the DLC textures live in
// content packs (Data\<DLC>\Assets.zip as X.psd.dds), which FilesystemFile.Exists does not see, so every DLC texture
// came back null. New prefix: run vanilla when the file is not on disk or the custom load returned null.
var gsPrefix = module.GetType("GraphicSetter.Patches.TextureLoadingPatch/LoadTexture_Patch")?.Methods.FirstOrDefault(m => m.Name == "Prefix" && m.Parameters.Count == 2);
if (gsPrefix != null)
{
    var customLoad = gsPrefix.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().First(m => m.Name == "CustomLoad");
    var exists = new MethodReference("get_Exists", ts.Boolean, gsPrefix.Parameters[0].ParameterType) { HasThis = true };
    var body = gsPrefix.Body;
    body.Instructions.Clear(); body.ExceptionHandlers.Clear(); body.Variables.Clear();
    var il = body.GetILProcessor();
    var onDisk = il.Create(OpCodes.Ldarg_1);
    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, exists); il.Emit(OpCodes.Brtrue_S, onDisk);
    il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
    il.Append(onDisk); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, customLoad); il.Emit(OpCodes.Stind_Ref);
    il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldind_Ref); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
    Hit("GraphicSetter LoadTexture prefix -> vanilla fallback");
}

// More Vanilla Mechanoid: GetCachedFieldInfo uses obj.GetType().GetField(name, flags), which misses private fields
// declared on a base class (ProjectileProperties.damageAmountBase on Shrapnel/MatchLock subclasses).
// Route it through an injected FindField that walks BaseType with DeclaredOnly.
var mvm = module.GetType("MoreVanillaMechanoid.MVM_ReflectionHelper");
var mvmGet = mvm?.Methods.FirstOrDefault(m => m.Name == "GetCachedFieldInfo" && m.HasBody);
if (mvmGet != null)
{
    var gfIns = mvmGet.Body.Instructions.Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference r && r.Name == "GetField"
        && r.DeclaringType.FullName == "System.Type" && r.Parameters.Count == 2).ToList();
    if (gfIns.Count != 1) throw new Exception("MVM GetCachedFieldInfo: expected one Type.GetField(string, BindingFlags), found " + gfIns.Count);
    var gf = (MethodReference)gfIns[0].Operand;
    var typeT = gf.DeclaringType; var flagsT = gf.Parameters[1].ParameterType;
    var ff = new MethodDefinition("FindField", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, gf.ReturnType);
    ff.Parameters.Add(new ParameterDefinition("t", ParameterAttributes.None, typeT));
    ff.Parameters.Add(new ParameterDefinition("n", ParameterAttributes.None, ts.String));
    ff.Parameters.Add(new ParameterDefinition("f", ParameterAttributes.None, flagsT));
    var fil = ff.Body.GetILProcessor();
    var top = fil.Create(OpCodes.Ldarg_0); var none = fil.Create(OpCodes.Ldnull); var found = fil.Create(OpCodes.Ret);
    fil.Append(top); fil.Emit(OpCodes.Brfalse, none);
    fil.Emit(OpCodes.Ldarg_0); fil.Emit(OpCodes.Ldarg_1); fil.Emit(OpCodes.Ldarg_2); fil.Emit(OpCodes.Ldc_I4_2); fil.Emit(OpCodes.Or); // | DeclaredOnly
    fil.Emit(OpCodes.Callvirt, gf); fil.Emit(OpCodes.Dup); fil.Emit(OpCodes.Brtrue, found); fil.Emit(OpCodes.Pop);
    fil.Emit(OpCodes.Ldarg_0); fil.Emit(OpCodes.Callvirt, new MethodReference("get_BaseType", typeT, typeT) { HasThis = true });
    fil.Emit(OpCodes.Starg_S, ff.Parameters[0]); fil.Emit(OpCodes.Br, top);
    fil.Append(found); fil.Append(none); fil.Emit(OpCodes.Ret);
    mvm.Methods.Add(ff);
    gfIns[0].OpCode = OpCodes.Call; gfIns[0].Operand = ff;
    Hit("MVM GetCachedFieldInfo -> base-walking FindField");
}

// UsefulMarks: looks for CM Color Coded Mood Bar's assembly by its 1.6 name; on 1.7 it is ColoredMoodBar17 (same types).
var um = module.GetType("UsefulMarks.UsefulMarksMod")?.Methods.FirstOrDefault(m => m.Name == "PatchCMCCMB" && m.HasBody);
if (um != null)
    foreach (var i in um.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "CMColoredMoodBar"))
    { i.Operand = "ColoredMoodBar17"; Hit("UsefulMarks CMColoredMoodBar -> ColoredMoodBar17"); }

// Isekai Leveling: AccessTools.Method(MindWipe, "Cast") is ambiguous (inherited obsolete overload); the override is declared on MindWipe.
var isk = module.GetType("IsekaiLeveling.Quests.Patch_TitleDuelFocus_VPEMindWipe");
if (isk != null)
    foreach (var m in isk.Methods.Where(m => m.HasBody))
        foreach (var i in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r && r.Name == "Method"
            && r.DeclaringType.FullName == "HarmonyLib.AccessTools" && r.Parameters.Count == 4))
        {
            var r = (MethodReference)i.Operand;
            var d = new MethodReference("DeclaredMethod", r.ReturnType, r.DeclaringType) { HasThis = false };
            foreach (var p in r.Parameters) d.Parameters.Add(new ParameterDefinition(p.ParameterType));
            i.Operand = d; Hit("Isekai MindWipe AccessTools.Method -> DeclaredMethod");
        }

// Eccentric Projectiles: its DrawEquipmentAndApparelExtras transpiler hooks only the first DrawEquipmentAiming call and warns on
// more; Dual Wield (priority 600, runs first) adds the off-hand call. Make the "already found" test constant false so every
// aiming call gets the stack-neutral SetWeaponAiming() hook and the warning branch is dead.
foreach (var m in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
{
    var warn = m.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldstr && ((string)i.Operand).StartsWith("(Eccentric Projectiles) Found more than one call to DrawEquipmentAiming"));
    if (warn == null) continue;
    var br = m.Body.Instructions.Where(i => (i.OpCode == OpCodes.Brtrue || i.OpCode == OpCodes.Brtrue_S) && i.Operand == warn).ToList();
    if (br.Count != 1 || br[0].Previous == null || !br[0].Previous.OpCode.Name.StartsWith("ldloc"))
        throw new Exception("EP transpiler: unexpected IL around the multiple-call warning");
    br[0].Previous.OpCode = OpCodes.Ldc_I4_0; br[0].Previous.Operand = null;
    Hit("EccentricProjectiles: hook every DrawEquipmentAiming call");
}

// Deep Swimming: Waylith runs Unity shaders only from a bundle's Vulkan programs. BundleFor's default branch picks the Windows
// bundle (D3D11 only), which ApiFitsBundle rejects on Vulkan, so the mod falls back. Use the _linux bundle (OpenGLCore + Vulkan
// programs) and accept any graphics API.
var dsAssets = module.GetType("DeepSwimming.DeepSwimmingAssets");
var dsBundle = dsAssets?.Methods.FirstOrDefault(m => m.Name == "BundleFor" && m.HasBody);
if (dsBundle != null)
    foreach (var i in dsBundle.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "deepswimming"))
    { i.Operand = "deepswimming_linux"; Hit("DeepSwimming BundleFor default -> deepswimming_linux"); }
var dsApi = dsAssets?.Methods.FirstOrDefault(m => m.Name == "ApiFitsBundle" && m.HasBody);
if (dsApi != null && dsApi.Body.Instructions[0].OpCode != OpCodes.Ldc_I4_1)
{
    var dil = dsApi.Body.GetILProcessor(); var first = dsApi.Body.Instructions[0];
    dil.InsertBefore(first, dil.Create(OpCodes.Ldc_I4_1)); dil.InsertBefore(first, dil.Create(OpCodes.Ret));
    Hit("DeepSwimming ApiFitsBundle -> true");
}

// --scos FILE: add [Verse.StaticConstructorOnStartup] to the listed types (one full name per line, # comments) so vanilla
// runs their static constructors on the main thread at startup (fixes the dev-mode "probably needs a
// StaticConstructorOnStartup attribute" report). The list must only hold types whose .cctor is safe after defs load.
for (int i = 2; i + 1 < args.Length; i += 2)
    if (args[i] == "--scos")
    {
        var want = File.ReadAllLines(args[i + 1]).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToHashSet();
        foreach (var t in module.GetTypes().Where(t => want.Contains(t.FullName)))
        {
            if (t.CustomAttributes.Any(a => a.AttributeType.FullName == "Verse.StaticConstructorOnStartup")) continue;
            var verse = module.AssemblyReferences.First(a => a.Name == "Assembly-CSharp");
            var attr = new TypeReference("Verse", "StaticConstructorOnStartup", module, verse);
            t.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor", ts.Void, attr) { HasThis = true }));
            Hit("[StaticConstructorOnStartup] " + t.FullName);
        }
    }

foreach (var type in module.GetTypes().ToList())
    foreach (var method in type.Methods.Where(m => m.HasBody))
    {
        var body = method.Body;
        body.SimplifyMacros();
        var il = body.GetILProcessor();
        foreach (var ins in body.Instructions.ToList())
        {
            if (ins.Operand is MethodReference m && (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt))
            {
                var dt = m.DeclaringType.GetElementType().FullName;
                // .NET List<T>.EnsureCapacity returns int (Mono's was void)
                if (dt == "System.Collections.Generic.List`1" && m.Name == "EnsureCapacity" && m.ReturnType.MetadataType == MetadataType.Void)
                {
                    var r = new MethodReference(m.Name, ts.Int32, m.DeclaringType) { HasThis = m.HasThis };
                    foreach (var p in m.Parameters) r.Parameters.Add(new ParameterDefinition(p.ParameterType));
                    ins.Operand = r;
                    il.InsertAfter(ins, il.Create(OpCodes.Pop));
                    Hit("List.EnsureCapacity void->int + pop");
                }
                // UnityEngine.Input.ResetInputAxes is not in the 1.7 Unity stubs; it only clears held axes
                else if (dt == "UnityEngine.Input" && m.Name == "ResetInputAxes" && m.Parameters.Count == 0)
                {
                    ins.OpCode = OpCodes.Nop; ins.Operand = null;
                    Hit("Input.ResetInputAxes -> nop");
                }
                // Widgets.ThingIcon(Rect, Thing, float, Rot4?, bool) gained (float scale = 1, bool grayscale = false)
                else if (dt == "Verse.Widgets" && m.Name == "ThingIcon" && m.Parameters.Count == 5 && m.Parameters[1].ParameterType.FullName == "Verse.Thing")
                {
                    var r = new MethodReference(m.Name, m.ReturnType, m.DeclaringType) { HasThis = false };
                    foreach (var p in m.Parameters) r.Parameters.Add(new ParameterDefinition(p.ParameterType));
                    r.Parameters.Add(new ParameterDefinition(ts.Single));
                    r.Parameters.Add(new ParameterDefinition(ts.Boolean));
                    var call = il.Create(OpCodes.Call, r);
                    ins.OpCode = OpCodes.Ldc_R4; ins.Operand = 1f;
                    il.InsertAfter(ins, call);
                    il.InsertAfter(ins, il.Create(OpCodes.Ldc_I4_0));
                    Hit("Widgets.ThingIcon 5 -> 7 args");
                }
                // UnityStubs' Object.hideFlags is set-only and its setter just logs "Stub!"; pop receiver and value (same stack effect)
                else if (dt == "UnityEngine.Object" && m.Name == "set_hideFlags")
                {
                    if (ins.Previous?.OpCode == OpCodes.Constrained) throw new Exception($"{method.FullName}: constrained. prefix on set_hideFlags");
                    ins.OpCode = OpCodes.Pop; ins.Operand = null;
                    il.InsertAfter(ins, il.Create(OpCodes.Pop));
                    Hit("Object.hideFlags setter -> pop pop");
                }
            }
            else if (ins.Operand is FieldReference f)
            {
                var dt = f.DeclaringType.FullName;
                // Mono's Label.label is m_label on .NET (private); Label.GetHashCode() returns it
                if (dt == "System.Reflection.Emit.Label" && f.Name == "label")
                {
                    if (ins.OpCode != OpCodes.Ldfld) throw new Exception($"{method.FullName}: unsupported {ins.OpCode} on Label::label");
                    var gh = new MethodReference("GetHashCode", ts.Int32, ts.Object) { HasThis = true };
                    ins.OpCode = OpCodes.Box; ins.Operand = f.DeclaringType;
                    il.InsertAfter(ins, il.Create(OpCodes.Callvirt, gh));
                    Hit("Emit.Label.label -> box + GetHashCode");
                }
                else if (dt == "Verse.TerrainDef" && f.Name == "holdSnow")
                {
                    ins.Operand = new FieldReference("holdSnowOrSand", f.FieldType, f.DeclaringType);
                    Hit("TerrainDef.holdSnow -> holdSnowOrSand");
                }
                else if (dt == "Replace_Stuff.NewThing.NewThingDefOf")
                {
                    ins.Operand = DefOfField(f);
                    Hit("NewThingDefOf -> own [DefOf] class");
                }
            }
        }
        body.OptimizeMacros();
    }

// Harmony 2's 0Harmony carried a DefaultInterpolatedStringHandler polyfill; Harmony 3 does not. Use the runtime's.
foreach (var tr in module.GetTypeReferences().ToList())
    if (tr.Scope?.Name == "0Harmony" && tr.Namespace == "System.Runtime.CompilerServices")
    {
        var rt = module.AssemblyReferences.FirstOrDefault(a => a.Name == "System.Runtime");
        if (rt == null)
        {
            rt = new AssemblyNameReference("System.Runtime", new Version(8, 0, 0, 0)) { PublicKeyToken = new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a } };
            module.AssemblyReferences.Add(rt);
        }
        tr.Scope = rt;
        Hit("0Harmony polyfill " + tr.Name + " -> System.Runtime");
    }

foreach (var kv in hits) Console.WriteLine($"{kv.Value,4}  {kv.Key}");
if (hits.Count == 0) { Console.WriteLine("no rule matched; nothing written"); return 1; }
module.Write(args[1]);
return 0;
