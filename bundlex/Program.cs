// bundlex: export Texture2D assets from a Unity AssetBundle as PNG files, named by the bundle's container paths.
// Built for the Homebrew pack's Pack/textures bundles (BC7). Uses the game's own AssetsTools.NET copies.
// Usage: bundlex BUNDLE OUTDIR [NAMES.txt]   (NAMES: container paths to export, one per line; default all)
// Writes OUTDIR/<container path with .png extension>; prints exported/failed counts.
using System.IO.Compression;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

if (args.Length < 2) { Console.Error.WriteLine("usage: bundlex BUNDLE OUTDIR [NAMES.txt]"); return 2; }
HashSet<string> want = args.Length > 2 ? File.ReadAllLines(args[2]).Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0).ToHashSet() : null;
var am = new AssetsManager();
var bun = am.LoadBundleFile(args[0], true);
var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
var ab = afile.file.GetAssetsOfType(AssetClassID.AssetBundle).First();
var container = am.GetBaseField(afile, ab)["m_Container.Array"];
int ok = 0, fail = 0;
foreach (var entry in container.Children)
{
    string name = entry["first"].AsString;
    if (want != null && !want.Contains(name.ToLowerInvariant())) continue;
    long pathId = entry["second"]["asset"]["m_PathID"].AsLong;
    try
    {
        var tf = TextureFile.ReadTextureFile(am.GetBaseField(afile, pathId));
        if (!tf.SetPictureDataFromBundle(bun)) tf.FillPictureData(afile);
        var rgba = tf.DecodeTextureRaw(tf.FillPictureData(afile), useBgra: false);
        if (rgba == null) throw new Exception("unsupported format " + tf.m_TextureFormat);
        var outPath = Path.Combine(args[1], Path.ChangeExtension(name, ".png"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, Png.Encode(rgba, tf.m_Width, tf.m_Height));
        ok++;
    }
    catch (Exception e) { fail++; Console.Error.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"exported={ok} failed={fail}");
return fail == 0 ? 0 : 1;

static class Png
{
    static readonly uint[] Crc = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    // RGBA rows bottom-up (Unity order) -> PNG (top-down, 8-bit RGBA, no filter)
    public static byte[] Encode(byte[] rgba, int w, int h)
    {
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = h - 1; y >= 0; y--) { z.WriteByte(0); z.Write(rgba, y * w * 4, w * 4); }
        var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr); Chunk(ms, "IDAT", raw.ToArray()); Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }
    static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(td);
        uint c = 0xFFFFFFFF; foreach (var b in td) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        var crc = new byte[4]; BE(crc, 0, c ^ 0xFFFFFFFF); s.Write(crc);
    }
}
