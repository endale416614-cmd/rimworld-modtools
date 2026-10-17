using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection.Emit;
using System.Text;

var path = args[0];
var names = args.Skip(1).ToHashSet();
using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

var one = new Dictionary<int, OpCode>(); var two = new Dictionary<int, OpCode>();
foreach (var f in typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
{
    var oc = (OpCode)f.GetValue(null);
    ushort v = (ushort)oc.Value;
    if (oc.Size == 1) one[v] = oc; else two[v & 0xFF] = oc;
}

string TypeName(EntityHandle h)
{
    switch (h.Kind)
    {
        case HandleKind.TypeDefinition: {
            var t = md.GetTypeDefinition((TypeDefinitionHandle)h);
            var n = md.GetString(t.Name); var ns = md.GetString(t.Namespace);
            var d = t.GetDeclaringType();
            if (!d.IsNil) return TypeName(d) + "/" + n;
            return ns.Length > 0 ? ns + "." + n : n; }
        case HandleKind.TypeReference: {
            var t = md.GetTypeReference((TypeReferenceHandle)h);
            var ns = md.GetString(t.Namespace); var n = md.GetString(t.Name);
            return ns.Length > 0 ? ns + "." + n : n; }
        case HandleKind.TypeSpecification: return "typespec";
        default: return h.Kind.ToString();
    }
}
string Tok(int token)
{
    EntityHandle h = default; if ((token >> 24) != 0x70) h = MetadataTokens.EntityHandle(token);
    int tbl = token >> 24;
    try {
    switch (tbl)
    {
        case 0x70: return "\"" + md.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF)).Replace("\n","\n") + "\"";
        case 0x0A: { var m = md.GetMemberReference((MemberReferenceHandle)h); return TypeName(m.Parent) + "::" + md.GetString(m.Name); }
        case 0x06: { var m = md.GetMethodDefinition((MethodDefinitionHandle)h); return TypeName(m.GetDeclaringType()) + "::" + md.GetString(m.Name); }
        case 0x04: { var f = md.GetFieldDefinition((FieldDefinitionHandle)h); return TypeName(f.GetDeclaringType()) + "::" + md.GetString(f.Name); }
        case 0x2B: { var ms = md.GetMethodSpecification((MethodSpecificationHandle)h); return Tok(MetadataTokens.GetToken(ms.Method)) + "<>"; }
        case 0x01: case 0x02: case 0x1B: return TypeName(h);
        default: return "tok_" + token.ToString("X8");
    } } catch (Exception e) { return "tok_" + token.ToString("X8") + "!" + e.Message; }
}

string Dump(MethodDefinition m)
{
    var sb = new StringBuilder();
    if (m.RelativeVirtualAddress == 0) return "(no body)";
    var body = pe.GetMethodBody(m.RelativeVirtualAddress);
    var il = body.GetILBytes(); int i = 0;
    while (i < il.Length)
    {
        int off = i; int b = il[i++]; OpCode oc;
        if (b == 0xFE) { oc = two[il[i++]]; } else oc = one[b];
        string op = "";
        switch (oc.OperandType)
        {
            case OperandType.InlineNone: break;
            case OperandType.ShortInlineBrTarget: { int d = (sbyte)il[i]; i++; op = "IL_" + (i + d).ToString("X4"); break; }
            case OperandType.InlineBrTarget: { int d = BitConverter.ToInt32(il, i); i += 4; op = "IL_" + (i + d).ToString("X4"); break; }
            case OperandType.ShortInlineI: op = ((sbyte)il[i]).ToString(); i++; break;
            case OperandType.ShortInlineVar: op = il[i].ToString(); i++; break;
            case OperandType.InlineVar: op = BitConverter.ToUInt16(il, i).ToString(); i += 2; break;
            case OperandType.InlineI: op = BitConverter.ToInt32(il, i).ToString(); i += 4; break;
            case OperandType.InlineI8: op = BitConverter.ToInt64(il, i).ToString(); i += 8; break;
            case OperandType.ShortInlineR: op = BitConverter.ToSingle(il, i).ToString(); i += 4; break;
            case OperandType.InlineR: op = BitConverter.ToDouble(il, i).ToString(); i += 8; break;
            case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); i += 4; int bas = i + 4 * n; var l = new List<string>(); for (int k = 0; k < n; k++) { l.Add("IL_" + (bas + BitConverter.ToInt32(il, i)).ToString("X4")); i += 4; } op = string.Join(",", l); break; }
            default: { int t = BitConverter.ToInt32(il, i); i += 4; op = Tok(t); break; }
        }
        sb.AppendLine($"  IL_{off:X4}: {oc.Name} {op}");
    }
    return sb.ToString();
}

// scan for writers
var targets = new[] { "ModMainPath", "ModAssemblyPath", "ModScriptsPath" };
foreach (var mh in md.MethodDefinitions)
{
    var m = md.GetMethodDefinition(mh);
    var mn = md.GetString(m.Name);
    bool hit = names.Contains(mn);
    if (!hit && m.RelativeVirtualAddress != 0)
    {
        var d = Dump(m);
        foreach (var t in targets) 
    }
    if (hit) { Console.WriteLine($"=== {TypeName(m.GetDeclaringType())}::{mn}"); Console.WriteLine(Dump(m)); }
}
