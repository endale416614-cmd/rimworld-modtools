// refcheck: static binary-compatibility check of RimWorld mod assemblies against the installed game.
// For every mod DLL it resolves each TypeRef and MemberRef (method/field, with full signature) against
// the game's assemblies (Assembly-CSharp, UnityStubs, the bundled .NET runtime) and against other mods'
// assemblies (highest AssemblyVersion wins, game copies win over mod copies). Anything that does not
// resolve would throw TypeLoad/MissingMethod/MissingField when the calling code is JIT-compiled.
// Never loads or runs the scanned code (System.Reflection.Metadata only).
// Usage: refcheck --game DIR --out FILE.json [--active PACKAGEIDS.txt] [--lib DIR]... MODROOT [MODROOT...]
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string game = null, outPath = null; var roots = new List<string>(); HashSet<string> active = null; var libs = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--game") game = args[++i];
    else if (args[i] == "--out") outPath = args[++i];
    else if (args[i] == "--lib") libs.Add(args[++i]);
    else if (args[i] == "--active") active = File.ReadAllLines(args[++i]).Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToHashSet();
    else roots.Add(args[i]);
}
if (game == null || outPath == null || roots.Count == 0) { Console.Error.WriteLine("usage: refcheck --game DIR --out FILE.json MODROOT [MODROOT...]"); return 2; }

var reg = new Registry();
foreach (var f in Directory.GetFiles(game, "*.dll")) reg.Add(f, null, isGame: true);
// --lib DIR: assemblies that load before every mod (e.g. a replacement Harmony); they win like game assemblies
foreach (var l in libs) foreach (var f in Directory.GetFiles(l, "*.dll")) reg.Add(f, null, isGame: true);

// mods: any folder with About/About.xml. DLLs = <load folder>/Assemblies/**/*.dll for the load folders the
// game would use on 1.7: LoadFolders.xml entry v1.7 (else the newest older one), IfModActive/IfModNotActive
// judged against --active (all included without it); no LoadFolders.xml = root + newest version folder <= 1.7.
var verRx = new Regex(@"^v?(\d+)\.(\d+)$");
var modDlls = new List<(string mod, string pid, string dll)>();
foreach (var root in roots)
    foreach (var about in Directory.EnumerateFiles(root, "About.xml", SearchOption.AllDirectories))
    {
        var aboutDir = Path.GetDirectoryName(about);
        if (!Path.GetFileName(aboutDir).Equals("About", StringComparison.OrdinalIgnoreCase)) continue;
        var mod = Path.GetDirectoryName(aboutDir);
        string pid;
        try { pid = (XDocument.Load(about).Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("packageId", StringComparison.OrdinalIgnoreCase))?.Value ?? "").Trim().ToLowerInvariant(); }
        catch { pid = ""; }
        if (pid.Length == 0) pid = "?" + Path.GetFileName(mod);
        if (active != null && !active.Contains(pid)) continue;
        foreach (var folder in Enumerable.Reverse(LoadFolders(mod))) // later entries load first, so they win same-name ties
        {
            var asmDir = Path.Combine(mod, folder, "Assemblies");
            if (!Directory.Exists(asmDir)) continue;
            foreach (var d in Directory.EnumerateFiles(asmDir, "*.dll", SearchOption.AllDirectories))
                if (reg.Add(d, mod, isGame: false)) modDlls.Add((mod, pid, d));
        }
    }

List<string> LoadFolders(string mod)
{
    (int, int)? Ver(string n) { var m = verRx.Match(n); return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null; }
    var lf = Directory.EnumerateFiles(mod, "*", SearchOption.TopDirectoryOnly).FirstOrDefault(f => Path.GetFileName(f).Equals("LoadFolders.xml", StringComparison.OrdinalIgnoreCase));
    if (lf != null)
    {
        try
        {
            var entries = XDocument.Load(lf).Root.Elements().Select(e => (v: Ver(e.Name.LocalName), e)).Where(x => x.v != null && x.v.Value.CompareTo((1, 7)) <= 0).ToList();
            if (entries.Count > 0)
            {
                var e = entries.OrderBy(x => x.v.Value).Last().e;
                bool Any(string ids) => ids.Split(',').Select(x => x.Trim().ToLowerInvariant()).Any(x => active == null || active.Contains(x));
                return e.Elements().Where(li =>
                {
                    var on = li.Attribute("IfModActive"); var off = li.Attribute("IfModNotActive");
                    if (on != null && !Any(on.Value)) return false;
                    if (off != null && active != null && Any(off.Value)) return false;
                    return true;
                }).Select(li => li.Value.Trim().Trim('/', '\\')).Distinct().ToList();
            }
        }
        catch { }
    }
    var best = Directory.GetDirectories(mod).Select(d => Ver(Path.GetFileName(d))).Where(v => v != null && v.Value.CompareTo((1, 7)) <= 0).Max();
    var res = new List<string> { "" };
    if (best != null) res.Add(Directory.GetDirectories(mod).Select(Path.GetFileName).First(n => Ver(n) == best));
    return res;
}

var report = new List<object>();
var agg = new Dictionary<string, (int n, HashSet<string> mods)>();
foreach (var (mod, pid, dll) in modDlls)
{
    var a = reg.ByPath[dll];
    var issues = new List<string>();
    if (reg.GameNames.Contains(a.Name)) { report.Add(new { mod, pid, dll, assembly = a.Name, skipped = "copy of a game/runtime assembly; the game's own copy is used" }); continue; }
    if (reg.ByName[a.Name] != a) { report.Add(new { mod, pid, dll, assembly = a.Name, skipped = "shadowed by " + reg.ByName[a.Name].Path }); continue; }
    var chk = new Checker(reg, a);
    issues.AddRange(chk.Run());
    foreach (var s in issues)
    {
        if (!agg.TryGetValue(s, out var v)) v = (0, new HashSet<string>());
        v.n++; v.mods.Add(pid); agg[s] = v;
    }
    report.Add(new { mod, pid, dll, assembly = a.Name, version = a.Version, issues });
}
var opts = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(outPath, JsonSerializer.Serialize(new
{
    game, roots, dlls = modDlls.Count,
    byTarget = agg.OrderByDescending(k => k.Value.mods.Count).Select(k => new { issue = k.Key, mods = k.Value.mods.OrderBy(x => x).ToList() }),
    assemblies = report
}, opts));
int bad = report.Count(r => r.GetType().GetProperty("issues")?.GetValue(r) is List<string> l && l.Count > 0);
Console.WriteLine($"dlls={modDlls.Count} with_issues={bad} distinct_issues={agg.Count} -> {outPath}");
return 0;

class Asm
{
    public string Name, Path, Version, Mod; public bool IsGame; public MetadataReader R;
    public Dictionary<string, TypeDefinitionHandle> Types = new();
    public Dictionary<string, string> Forwards = new(); // full type name -> target assembly name
}

class Registry
{
    public Dictionary<string, Asm> ByName = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Asm> ByPath = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> GameNames = new(StringComparer.OrdinalIgnoreCase);

    public bool Add(string path, string mod, bool isGame)
    {
        PEReader pe;
        try { pe = new PEReader(File.OpenRead(path), PEStreamOptions.PrefetchEntireImage); if (!pe.HasMetadata) return false; }
        catch { return false; }
        MetadataReader r;
        try { r = pe.GetMetadataReader(); if (!r.IsAssembly) return false; } catch { return false; }
        var def = r.GetAssemblyDefinition();
        var a = new Asm { Name = r.GetString(def.Name), Path = path, Version = def.Version.ToString(), Mod = mod, IsGame = isGame, R = r };
        foreach (var h in r.TypeDefinitions) a.Types[Names.Def(r, h)] = h;
        foreach (var h in r.ExportedTypes)
        {
            var e = r.GetExportedType(h);
            if (e.Implementation.Kind == HandleKind.AssemblyReference)
                a.Forwards[Names.Join(r.GetString(e.Namespace), r.GetString(e.Name))] = r.GetString(r.GetAssemblyReference((AssemblyReferenceHandle)e.Implementation).Name);
        }
        ByPath[path] = a;
        if (isGame) GameNames.Add(a.Name);
        if (ByName.TryGetValue(a.Name, out var old) && (old.IsGame || (!isGame && Version.Parse(old.Version) >= def.Version))) return true;
        ByName[a.Name] = a;
        return true;
    }

    // Unity module refs (UnityEngine.CoreModule, ...) all map onto the engine's stub assemblies
    public IEnumerable<Asm> Candidates(string asmName)
    {
        if (ByName.TryGetValue(asmName, out var a)) yield return a;
        if (asmName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) || asmName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase))
            foreach (var n in new[] { "UnityStubs", "Waylith.UnityCompat", "Assembly-CSharp" })
                if (ByName.TryGetValue(n, out var s)) yield return s;
        if (asmName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) && ByName.TryGetValue("Assembly-CSharp", out var g)) yield return g;
    }

    public (Asm, TypeDefinitionHandle)? FindType(string asmName, string fullName, int depth = 0)
    {
        if (depth > 8) return null;
        foreach (var a in Candidates(asmName))
        {
            if (a.Types.TryGetValue(fullName, out var h)) return (a, h);
            var top = fullName.Split('/')[0];
            if (a.Forwards.TryGetValue(top, out var to)) { var f = FindType(to, fullName, depth + 1); if (f != null) return f; }
        }
        return null;
    }
}

static class Names
{
    public static string Join(string ns, string n) => ns.Length == 0 ? n : ns + "." + n;
    public static string Def(MetadataReader r, TypeDefinitionHandle h)
    {
        var t = r.GetTypeDefinition(h);
        var dt = t.GetDeclaringType();
        return dt.IsNil ? Join(r.GetString(t.Namespace), r.GetString(t.Name)) : Def(r, dt) + "/" + r.GetString(t.Name);
    }
    // returns (assembly name or null for local, full name)
    public static (string asm, string name) Ref(MetadataReader r, TypeReferenceHandle h)
    {
        var t = r.GetTypeReference(h);
        var n = r.GetString(t.Name);
        switch (t.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                return (r.GetString(r.GetAssemblyReference((AssemblyReferenceHandle)t.ResolutionScope).Name), Join(r.GetString(t.Namespace), n));
            case HandleKind.TypeReference:
                var p = Ref(r, (TypeReferenceHandle)t.ResolutionScope);
                return (p.asm, p.name + "/" + n);
            default: return (null, Join(r.GetString(t.Namespace), n));
        }
    }
}

// canonical signature strings, identical for a ref in the mod and a def in the target
class Sig : ISignatureTypeProvider<string, object>
{
    public static readonly Sig I = new();
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr(" + string.Join(",", s.ParameterTypes) + ")" + s.ReturnType;
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool req) => req ? u + " modreq(" + m + ")" : u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => "System." + (t == PrimitiveTypeCode.TypedReference ? "TypedReference" : t.ToString());
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => Names.Def(r, h);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => Names.Ref(r, h).name;
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
    public static string Method(MethodSignature<string> s) => (s.GenericParameterCount > 0 ? "``" + s.GenericParameterCount : "") + "(" + string.Join(",", s.ParameterTypes) + ")" + s.ReturnType;
}

class Checker
{
    readonly Registry reg; readonly Asm a; readonly MetadataReader r;
    public Checker(Registry reg, Asm a) { this.reg = reg; this.a = a; r = a.R; }

    (Asm, TypeDefinitionHandle)? Resolve(MetadataReader rr, TypeReferenceHandle h, out string why)
    {
        var (asm, name) = Names.Ref(rr, h);
        why = null;
        if (asm == null) return null;
        if (!reg.Candidates(asm).Any()) { why = $"MissingAssembly {asm}"; return null; }
        var t = reg.FindType(asm, name);
        if (t == null) why = $"MissingType [{asm}]{name}";
        return t;
    }

    public List<string> Run()
    {
        var seen = new HashSet<string>(); var outl = new List<string>();
        void Add(string s) { if (s != null && seen.Add(s)) outl.Add(s); }
        foreach (var h in r.TypeReferences)
        {
            try { Resolve(r, h, out var why); Add(why); }
            catch (Exception e) { Add($"Unreadable TypeRef: {e.Message}"); }
        }
        foreach (var h in r.MemberReferences)
        {
            try { Add(CheckMember(h)); }
            catch (Exception e) { Add($"Unreadable MemberRef: {e.Message}"); }
        }
        return outl;
    }

    string CheckMember(MemberReferenceHandle h)
    {
        var m = r.GetMemberReference(h);
        TypeReferenceHandle? parent = m.Parent.Kind switch
        {
            HandleKind.TypeReference => (TypeReferenceHandle)m.Parent,
            HandleKind.TypeSpecification => GenericBase(r.GetTypeSpecification((TypeSpecificationHandle)m.Parent).Signature),
            _ => null
        };
        if (parent == null) return null;
        var t = Resolve(r, parent.Value, out _);
        if (t == null) return null; // type problem already reported
        var name = r.GetString(m.Name);
        var (pa, pn) = Names.Ref(r, parent.Value);
        string issue = m.GetKind() == MemberReferenceKind.Method
            ? CheckMethod(t.Value, name, Sig.Method(m.DecodeMethodSignature(Sig.I, null)))
            : CheckField(t.Value, name, m.DecodeFieldSignature(Sig.I, null));
        return issue == null ? null : $"{issue} [{pa}]{pn}::{name}";
    }

    TypeReferenceHandle? GenericBase(BlobHandle sig)
    {
        var b = r.GetBlobReader(sig);
        if (b.ReadByte() != 0x15) return null; // not GENERICINST (arrays etc.)
        b.ReadByte(); // CLASS / VALUETYPE
        var h = b.ReadTypeHandle();
        return h.Kind == HandleKind.TypeReference ? (TypeReferenceHandle)h : null;
    }

    IEnumerable<(Asm, TypeDefinition)> Chain((Asm, TypeDefinitionHandle) start)
    {
        var cur = (Asm: start.Item1, H: start.Item2);
        for (int i = 0; i < 32; i++)
        {
            var td = cur.Asm.R.GetTypeDefinition(cur.H);
            yield return (cur.Asm, td);
            var bt = td.BaseType;
            if (bt.IsNil) yield break;
            if (bt.Kind == HandleKind.TypeSpecification)
            {
                var b = cur.Asm.R.GetBlobReader(cur.Asm.R.GetTypeSpecification((TypeSpecificationHandle)bt).Signature);
                if (b.ReadByte() != 0x15) yield break;
                b.ReadByte(); bt = b.ReadTypeHandle();
            }
            if (bt.Kind == HandleKind.TypeDefinition) { cur = (cur.Asm, (TypeDefinitionHandle)bt); continue; }
            if (bt.Kind != HandleKind.TypeReference) yield break;
            var (an, n) = Names.Ref(cur.Asm.R, (TypeReferenceHandle)bt);
            var next = an == null ? null : reg.FindType(an, n);
            if (next == null) yield break;
            cur = next.Value;
        }
    }

    string CheckMethod((Asm, TypeDefinitionHandle) t, string name, string sig)
    {
        var found = new List<string>();
        foreach (var (asm, td) in Chain(t))
            foreach (var mh in td.GetMethods())
            {
                var md = asm.R.GetMethodDefinition(mh);
                if (asm.R.GetString(md.Name) != name) continue;
                var s = Sig.Method(md.DecodeSignature(Sig.I, null));
                if (s == sig) return null;
                found.Add(s);
            }
        return found.Count == 0 ? $"MissingMethod {sig}" : $"MethodSigChanged want {sig} have {string.Join(" | ", found.Distinct())}";
    }

    string CheckField((Asm, TypeDefinitionHandle) t, string name, string type)
    {
        string have = null;
        foreach (var (asm, td) in Chain(t))
            foreach (var fh in td.GetFields())
            {
                var fd = asm.R.GetFieldDefinition(fh);
                if (asm.R.GetString(fd.Name) != name) continue;
                var s = fd.DecodeSignature(Sig.I, null);
                if (s == type) return null;
                have = s;
            }
        return have == null ? $"MissingField {type}" : $"FieldTypeChanged want {type} have {have}";
    }
}
