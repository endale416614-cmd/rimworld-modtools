# Records the RimWorld game window while a probe run plays, then cuts an animated GIF out of the recording using
# the "[PROBE] cut..." lines in the game log. Windows PowerShell 5.1; only .NET classes and Win32 calls, no extra tools.
#
#   gifcapture.ps1 -OutGif demo.gif -FramesDir .\frames [-Log Player.log] [-Fps 12.5] [-Width 640] [-Dither] [-Tolerance 0]
#   gifcapture.ps1 -EncodeOnly -OutGif demo.gif -FramesDir .\frames [-Log Player.log] [-Fps 12.5] [-Width 640] [-Dither] [-Tolerance 0]
#
# The second form does not capture: it re-reads the frame_<index>_<ticks>.png files in -FramesDir and the [PROBE] lines
# in -Log, applies the same cut ranges and encodes again (other -Fps or -Width, or a run that stopped before encoding).
# -Width is the GIF width; frames of another width are scaled to it (bicubic). With -EncodeOnly and no -Width the
# frames keep the width they have.
#
# Encoder (GifEncoder, below): one palette for the whole clip, built by median cut from a pixel sample of all kept
# frames. Index 255 is the transparent index, so colors quantize to 255 entries. Pixels map to the nearest entry
# through a cache keyed by the 5-6-5 reduced color; no dithering unless -Dither (ordered, 8x8) is given. Frame 1 is
# stored whole; every later frame only as the bounding rectangle of the pixels that changed since the previous one,
# with the unchanged pixels inside it set to the transparent index (disposal "leave in place"). LZW compressed.
#
# -Tolerance N (0 to 16, default 0 = off): a pixel whose color moved by at most N levels in every channel since it was
# last updated keeps its old color. Game footage often drifts by 1 or 2 levels over a third of the picture from one
# frame to the next (lighting, swaying plants), so without this nearly every frame differs nearly everywhere and
# differencing saves little. 2 to 4 hides that for a small loss in color accuracy.
#
# Exit codes: 0 GIF written, 1 unexpected failure, 2 game window never appeared, 3 no frames to encode.
# Dot-sourcing the file only defines the functions.
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(Mandatory)][string]$OutGif,
    [Parameter(Mandatory)][string]$FramesDir,
    [Parameter(Mandatory, ParameterSetName = 'Encode')][switch]$EncodeOnly,
    [string]$Log = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log'),
    [ValidateRange(1, 50)][double]$Fps = 12.5,
    [ValidateRange(16, 4096)][int]$Width = 640,
    [switch]$Dither,
    [ValidateRange(0, 16)][int]$Tolerance = 0,
    [Parameter(ParameterSetName = 'Capture')][ValidateRange(1, 86400)][int]$MaxSeconds = 600,
    [Parameter(ParameterSetName = 'Capture')][ValidateRange(0, 86400)][int]$WindowWaitSeconds = 240
)
$ErrorActionPreference = 'Stop'
$script:WidthGiven = $PSBoundParameters.ContainsKey('Width')
Add-Type -AssemblyName System.Drawing

if (-not ('GifCapNative' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public static class GifCapNative
{
    struct RECT { public int Left, Top, Right, Bottom; }
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    // {client screen X, Y, width, height, client origin inside the full-window bitmap X, Y, window width, height},
    // or null when the window is gone, minimized or has no client area.
    public static int[] Geometry(IntPtr hwnd)
    {
        RECT win, client;
        POINT origin = new POINT();
        if (IsIconic(hwnd) || !GetWindowRect(hwnd, out win) || !GetClientRect(hwnd, out client) || !ClientToScreen(hwnd, ref origin)) return null;
        int w = client.Right - client.Left, h = client.Bottom - client.Top;
        int ox = origin.X - win.Left, oy = origin.Y - win.Top, ww = win.Right - win.Left, wh = win.Bottom - win.Top;
        if (w <= 0 || h <= 0 || ox < 0 || oy < 0 || ox + w > ww || oy + h > wh) return null;
        return new int[] { origin.X, origin.Y, w, h, ox, oy, ww, wh };
    }
}

// Scales frames and writes them as PNG on one background thread, so grabbing the next frame is not held up by it.
// The queue is bounded: Add waits while 4 grabbed frames are still pending.
public sealed class GifCapWriter
{
    class Job { public Bitmap Src; public int W, H; public string Path; }

    readonly BlockingCollection<Job> queue = new BlockingCollection<Job>(4);
    readonly Thread worker;
    public int Failed;
    public string LastError;

    public GifCapWriter()
    {
        worker = new Thread(Run);
        worker.IsBackground = true;
        worker.Start();
    }

    // The writer takes ownership of src and disposes it.
    public void Add(Bitmap src, int w, int h, string path)
    {
        Job j = new Job();
        j.Src = src; j.W = w; j.H = h; j.Path = path;
        if (!queue.TryAdd(j, 10000)) throw new InvalidOperationException("The frame writer is not keeping up");
    }

    public void Finish()
    {
        queue.CompleteAdding();
        if (!worker.Join(120000)) throw new InvalidOperationException("The frame writer did not finish");
    }

    void Run()
    {
        foreach (Job j in queue.GetConsumingEnumerable())
        {
            try { Save(j); }
            catch (Exception e) { LastError = e.Message; Interlocked.Increment(ref Failed); }
            finally { j.Src.Dispose(); }
        }
    }

    // Bicubic copy of src at w x h, 24 bits per pixel. The caller disposes it.
    public static Bitmap Scale(Bitmap src, int w, int h)
    {
        Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        try
        {
            using (Graphics g = Graphics.FromImage(dst))
            using (ImageAttributes wrap = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                wrap.SetWrapMode(WrapMode.TileFlipXY);          // no dark fringe along the edges
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
            }
            return dst;
        }
        catch { dst.Dispose(); throw; }
    }

    static void Save(Job j)
    {
        using (Bitmap dst = Scale(j.Src, j.W, j.H)) dst.Save(j.Path, ImageFormat.Png);
    }
}

public sealed class GifResult
{
    public byte[] Data;
    public int Width, Height, Frames, Unchanged, Colors, Skipped, Unreadable, SampleStep;
    public long Samples;
    public double AvgRectPercent, AvgChangedPercent;     // over the frames after the first; an unchanged frame counts 0
    public string FirstError;
}

// Standard GIF LZW: minimum code size 8, clear code 256, end code 257, codes of 9 to 12 bits, a clear code
// when the table is full, data in sub-blocks of 255 bytes.
sealed class GifLzw
{
    const int HashBits = 13, HashSize = 1 << HashBits, HashMask = HashSize - 1;
    readonly int[] hashKey = new int[HashSize];
    readonly ushort[] hashCode = new ushort[HashSize];           // 0 = free slot, real codes start at 258
    readonly byte[] block = new byte[255];
    int blockLen, bitCount;
    uint bitBuffer;
    Stream dest;

    void PutByte(int b)
    {
        block[blockLen++] = (byte)b;
        if (blockLen == 255) FlushBlock();
    }

    void FlushBlock()
    {
        if (blockLen == 0) return;
        dest.WriteByte((byte)blockLen);
        dest.Write(block, 0, blockLen);
        blockLen = 0;
    }

    void Put(int code, int size)
    {
        bitBuffer |= (uint)code << bitCount;
        bitCount += size;
        while (bitCount >= 8)
        {
            PutByte((int)(bitBuffer & 0xFF));
            bitBuffer >>= 8;
            bitCount -= 8;
        }
    }

    // Writes the code size byte, the data sub-blocks and the zero length terminator for pix[0..n), n >= 1.
    public void Write(Stream s, byte[] pix, int n)
    {
        dest = s; blockLen = 0; bitCount = 0; bitBuffer = 0;
        s.WriteByte(8);
        Array.Clear(hashCode, 0, HashSize);
        int size = 9, free = 258;
        Put(256, size);
        int prefix = pix[0];
        for (int i = 1; i < n; i++)
        {
            int c = pix[i];
            int key = (prefix << 8) | c;
            int h = (int)(((uint)key * 2654435761u) >> (32 - HashBits));
            int hit = 0;
            while (hashCode[h] != 0)
            {
                if (hashKey[h] == key) { hit = hashCode[h]; break; }
                h = (h + 1) & HashMask;
            }
            if (hit != 0) { prefix = hit; continue; }
            Put(prefix, size);
            if (free < 4096)
            {
                hashKey[h] = key;
                hashCode[h] = (ushort)free;
                if (free == (1 << size)) size++;                 // codes get one bit wider right after entry 2^size is added
                free++;
            }
            else
            {
                Put(256, size);                                  // table full: start over
                Array.Clear(hashCode, 0, HashSize);
                size = 9; free = 258;
            }
            prefix = c;
        }
        Put(prefix, size);
        if (free < 4096 && free == (1 << size)) size++;          // a decoder adds an entry after the last code too
        Put(257, size);
        if (bitCount > 0) PutByte((int)(bitBuffer & 0xFF));
        FlushBlock();
        s.WriteByte(0);
    }
}

// Global-palette GIF encoder with frame differencing. Two passes over the frame files, so memory stays at a few
// frames however long the clip is: pass 1 samples pixels for the palette, pass 2 maps, differences and compresses.
public sealed class GifEncoder
{
    const int Transparent = 255;
    const long SampleBudget = 16000000;                          // pixels sampled for the palette, at most
    const double WeightPower = 1.0;                              // 1 = by pixel count; less than 1 favors rare colors
    const int DitherSpread = 24;                                 // ordered dithering shifts a color by +-12 levels
    static readonly int[] Bayer = BuildBayer();

    sealed class Box
    {
        public int Lo, Hi;                                       // range in order[]
        public double Mr, Mg, Mb, Sse;                           // weighted mean color and squared error around it
    }

    readonly long[] hn = new long[65536], hr = new long[65536], hg = new long[65536], hb = new long[65536];
    readonly int[] lut = new int[65536];                         // 5-6-5 key -> palette index, -1 = not looked up yet
    readonly byte[] pal = new byte[768];                         // global color table; entry 255 stays black, transparent
    double[] br, bg, bb, bw;                                     // per sampled 5-6-5 bin: mean color and weight
    int[] order;                                                 // bin numbers, regrouped by the box splits
    int colors;

    public static GifResult Encode(string[] paths, int width, int delayCs, bool dither, int tolerance, Action<string> log)
    {
        return new GifEncoder().Run(paths, width, delayCs, dither, tolerance, log);
    }

    static int[] BuildBayer()
    {
        int[] m = new int[] { 0 };
        for (int n = 1; n < 8; n *= 2)
        {
            int[] q = new int[4 * n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int v = 4 * m[y * n + x];
                    q[y * 2 * n + x] = v;
                    q[y * 2 * n + x + n] = v + 2;
                    q[(y + n) * 2 * n + x] = v + 3;
                    q[(y + n) * 2 * n + x + n] = v + 1;
                }
            m = q;
        }
        return m;
    }

    static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

    // Pixel step that keeps the sample near SampleBudget. A step sharing a factor with the width would only ever
    // look at some columns, so it is nudged up until they are coprime.
    static int SampleStep(long totalPixels, int width)
    {
        int s = (int)Math.Max(1, (totalPixels + SampleBudget - 1) / SampleBudget);
        while (s > 1 && Gcd(s, width) != 1) s++;
        return s;
    }

    // Frame as tightly packed BGR rows, scaled to w x h first when its own size differs.
    static void ReadBgr(Bitmap src, int w, int h, byte[] dst)
    {
        Bitmap work = src;
        if (src.Width != w || src.Height != h) work = GifCapWriter.Scale(src, w, h);
        try
        {
            BitmapData d = work.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int row = w * 3;
                for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), dst, y * row, row);
            }
            finally { work.UnlockBits(d); }
        }
        finally { if (!object.ReferenceEquals(work, src)) work.Dispose(); }
    }

    long Sample(byte[] px, int npx, int start, int step)
    {
        long n = 0;
        for (int p = start; p < npx; p += step)
        {
            int o = p * 3, b = px[o], g = px[o + 1], r = px[o + 2];
            int key = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
            hn[key]++; hr[key] += r; hg[key] += g; hb[key] += b;
            n++;
        }
        return n;
    }

    GifResult Run(string[] paths, int width, int delayCs, bool dither, int tolerance, Action<string> log)
    {
        GifResult res = new GifResult();
        Stopwatch clock = Stopwatch.StartNew();

        // Pass 1: geometry from the first readable frame, then a pixel sample of every frame for the palette.
        List<string> ok = new List<string>();
        byte[] px = null;
        int srcW = 0, srcH = 0, w = 0, h = 0, step = 1;
        foreach (string path in paths)
        {
            try
            {
                using (Bitmap src = new Bitmap(path))
                {
                    if (px == null)
                    {
                        srcW = src.Width; srcH = src.Height;
                        w = width > 0 ? width : srcW;
                        h = w == srcW ? srcH : Math.Max(2, 2 * (int)Math.Round((double)w * srcH / srcW / 2));
                        px = new byte[w * h * 3];
                        step = SampleStep((long)paths.Length * w * h, w);
                    }
                    else if (src.Width != srcW || src.Height != srcH) { res.Skipped++; continue; }
                    ReadBgr(src, w, h, px);
                }
            }
            catch (Exception e)
            {
                res.Unreadable++;
                if (res.FirstError == null) res.FirstError = Path.GetFileName(path) + ": " + e.Message;
                continue;
            }
            res.Samples += Sample(px, w * h, ok.Count % step, step);
            ok.Add(path);
        }
        if (ok.Count == 0) throw new InvalidOperationException("None of the " + paths.Length + " frame files could be read" + (res.FirstError == null ? "" : " (" + res.FirstError + ")"));
        res.Width = w; res.Height = h; res.Frames = ok.Count; res.SampleStep = step;
        BuildPalette();
        res.Colors = colors;
        if (log != null) log(string.Format("Palette: {0} colors from {1:N0} sampled pixels of {2} frames (every {3}th pixel), {4:N1} s",
            colors, res.Samples, ok.Count, step, clock.Elapsed.TotalSeconds));

        // Pass 2: map to the palette, difference against the previous frame, compress.
        MemoryStream ms = new MemoryStream();
        WriteHeader(ms, w, h);
        int npx = w * h;
        byte[] cur = new byte[npx], prev = new byte[npx], sub = new byte[npx];
        byte[] held = tolerance > 0 ? new byte[px.Length] : null;
        for (int i = 0; i < lut.Length; i++) lut[i] = -1;
        GifLzw lzw = new GifLzw();
        double rectSum = 0, changedSum = 0;
        for (int k = 0; k < ok.Count; k++)
        {
            using (Bitmap src = new Bitmap(ok[k])) ReadBgr(src, w, h, px);
            if (held != null) Calm(px, held, tolerance, k == 0);
            Map(px, cur, w, h, dither);
            if (k == 0)
            {
                WriteGce(ms, delayCs, false);
                WriteDescriptor(ms, 0, 0, w, h);
                lzw.Write(ms, cur, npx);
            }
            else
            {
                int x0 = w, x1 = -1, y0 = -1, y1 = -1;
                long changed = 0;
                for (int y = 0, row = 0; y < h; y++, row += w)
                {
                    int first = -1, last = -1;
                    for (int x = 0; x < w; x++)
                        if (cur[row + x] != prev[row + x]) { if (first < 0) first = x; last = x; changed++; }
                    if (first < 0) continue;
                    if (y0 < 0) y0 = y;
                    y1 = y;
                    if (first < x0) x0 = first;
                    if (last > x1) x1 = last;
                }
                WriteGce(ms, delayCs, true);
                if (y0 < 0)
                {
                    res.Unchanged++;                             // nothing changed: a single transparent pixel keeps the timing
                    sub[0] = Transparent;
                    WriteDescriptor(ms, 0, 0, 1, 1);
                    lzw.Write(ms, sub, 1);
                }
                else
                {
                    int rw = x1 - x0 + 1, rh = y1 - y0 + 1, n = 0;
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0, p = y * w + x0; x <= x1; x++, p++)
                            sub[n++] = cur[p] != prev[p] ? cur[p] : (byte)Transparent;
                    WriteDescriptor(ms, x0, y0, rw, rh);
                    lzw.Write(ms, sub, n);
                    rectSum += 100.0 * rw * rh / npx;
                    changedSum += 100.0 * changed / npx;
                }
            }
            byte[] t = prev; prev = cur; cur = t;
            if (log != null && (k + 1) % 100 == 0 && k + 1 < ok.Count)
                log(string.Format("Encoded {0} of {1} frames, {2:N0} KB so far, {3:N1} s", k + 1, ok.Count, ms.Length / 1024, clock.Elapsed.TotalSeconds));
        }
        ms.WriteByte(0x3B);
        if (ok.Count > 1) { res.AvgRectPercent = rectSum / (ok.Count - 1); res.AvgChangedPercent = changedSum / (ok.Count - 1); }
        res.Data = ms.ToArray();
        return res;
    }

    // Temporal noise filter for -Tolerance: a pixel that moved by at most tol levels in every channel since it was
    // last updated keeps that color. Rendering noise and slow lighting drift then stop changing the palette index
    // of most of the frame; real motion is far above the threshold and passes through.
    static void Calm(byte[] px, byte[] held, int tol, bool first)
    {
        if (first) { Buffer.BlockCopy(px, 0, held, 0, px.Length); return; }
        for (int o = 0; o < px.Length; o += 3)
        {
            int db = px[o] - held[o], dg = px[o + 1] - held[o + 1], dr = px[o + 2] - held[o + 2];
            if (db < -tol || db > tol || dg < -tol || dg > tol || dr < -tol || dr > tol)
            {
                held[o] = px[o]; held[o + 1] = px[o + 1]; held[o + 2] = px[o + 2];
            }
            else
            {
                px[o] = held[o]; px[o + 1] = held[o + 1]; px[o + 2] = held[o + 2];
            }
        }
    }

    // Each pixel to the palette entry nearest the color of its 5-6-5 bin; every key is looked up once.
    void Map(byte[] px, byte[] idx, int w, int h, bool dither)
    {
        for (int y = 0, i = 0, o = 0; y < h; y++)
        {
            int row = (y & 7) << 3;
            for (int x = 0; x < w; x++, i++, o += 3)
            {
                int b = px[o], g = px[o + 1], r = px[o + 2];
                if (dither)
                {
                    int off = ((2 * Bayer[row | (x & 7)] - 63) * DitherSpread) / 128;
                    b = Clamp(b + off); g = Clamp(g + off); r = Clamp(r + off);
                }
                int key = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
                int v = lut[key];
                if (v < 0) { v = Nearest(key); lut[key] = v; }
                idx[i] = (byte)v;
            }
        }
    }

    static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

    // Color of a key: the mean of the sampled pixels that fell into it, or the middle of its 5-6-5 cell.
    int Nearest(int key)
    {
        int r, g, b;
        long n = hn[key];
        if (n > 0) { r = (int)((hr[key] + n / 2) / n); g = (int)((hg[key] + n / 2) / n); b = (int)((hb[key] + n / 2) / n); }
        else
        {
            int r5 = key >> 11, g6 = (key >> 5) & 63, b5 = key & 31;
            r = (r5 << 3) | (r5 >> 2); g = (g6 << 2) | (g6 >> 4); b = (b5 << 3) | (b5 >> 2);
        }
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < colors; i++)
        {
            int dr = r - pal[3 * i], dg = g - pal[3 * i + 1], db = b - pal[3 * i + 2];
            int d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // Median cut over the sampled 5-6-5 bins. The box with the largest squared error is split next, along its
    // widest channel, where the two halves keep the least squared error. Palette entry = mean color of the box.
    void BuildPalette()
    {
        int m = 0;
        for (int k = 0; k < 65536; k++) if (hn[k] > 0) m++;
        br = new double[m]; bg = new double[m]; bb = new double[m]; bw = new double[m]; order = new int[m];
        for (int k = 0, j = 0; k < 65536; k++)
        {
            if (hn[k] == 0) continue;
            br[j] = (double)hr[k] / hn[k]; bg[j] = (double)hg[k] / hn[k]; bb[j] = (double)hb[k] / hn[k];
            bw[j] = Math.Pow(hn[k], WeightPower);
            order[j] = j;
            j++;
        }
        List<Box> boxes = new List<Box>();
        Box root = new Box();
        root.Lo = 0; root.Hi = m;
        Measure(root);
        boxes.Add(root);
        while (boxes.Count < Transparent)                        // entries 0..254 hold colors, 255 stays transparent
        {
            int pick = -1;
            double top = 0;
            for (int i = 0; i < boxes.Count; i++)
                if (boxes[i].Hi - boxes[i].Lo > 1 && boxes[i].Sse > top) { top = boxes[i].Sse; pick = i; }
            if (pick < 0) break;
            Box[] two = Split(boxes[pick]);
            boxes[pick] = two[0];
            boxes.Add(two[1]);
        }
        colors = boxes.Count;
        for (int i = 0; i < colors; i++)
        {
            pal[3 * i] = (byte)Clamp((int)Math.Round(boxes[i].Mr));
            pal[3 * i + 1] = (byte)Clamp((int)Math.Round(boxes[i].Mg));
            pal[3 * i + 2] = (byte)Clamp((int)Math.Round(boxes[i].Mb));
        }
    }

    void Measure(Box b)
    {
        double w = 0, s1r = 0, s1g = 0, s1b = 0, s2 = 0;
        for (int i = b.Lo; i < b.Hi; i++)
        {
            int j = order[i];
            double wt = bw[j];
            w += wt; s1r += wt * br[j]; s1g += wt * bg[j]; s1b += wt * bb[j];
            s2 += wt * (br[j] * br[j] + bg[j] * bg[j] + bb[j] * bb[j]);
        }
        b.Mr = s1r / w; b.Mg = s1g / w; b.Mb = s1b / w;
        b.Sse = s2 - (s1r * s1r + s1g * s1g + s1b * s1b) / w;
    }

    Box[] Split(Box b)
    {
        int n = b.Hi - b.Lo;
        double tw = 0, t1r = 0, t1g = 0, t1b = 0, t2r = 0, t2g = 0, t2b = 0;
        for (int i = b.Lo; i < b.Hi; i++)
        {
            int j = order[i];
            double wt = bw[j];
            tw += wt; t1r += wt * br[j]; t1g += wt * bg[j]; t1b += wt * bb[j];
            t2r += wt * br[j] * br[j]; t2g += wt * bg[j] * bg[j]; t2b += wt * bb[j] * bb[j];
        }
        double vr = t2r / tw - (t1r / tw) * (t1r / tw), vg = t2g / tw - (t1g / tw) * (t1g / tw), vb = t2b / tw - (t1b / tw) * (t1b / tw);
        double[] chan = (vr >= vg && vr >= vb) ? br : (vg >= vb ? bg : bb);
        int[] item = new int[n];
        float[] key = new float[n];
        for (int i = 0; i < n; i++) { item[i] = order[b.Lo + i]; key[i] = (float)chan[item[i]]; }
        Array.Sort(key, item);
        Array.Copy(item, 0, order, b.Lo, n);

        double lw = 0, l1r = 0, l1g = 0, l1b = 0, l2 = 0, t2 = t2r + t2g + t2b, best = double.MaxValue;
        int cut = 1;
        for (int i = 0; i < n - 1; i++)
        {
            int j = item[i];
            double wt = bw[j];
            lw += wt; l1r += wt * br[j]; l1g += wt * bg[j]; l1b += wt * bb[j];
            l2 += wt * (br[j] * br[j] + bg[j] * bg[j] + bb[j] * bb[j]);
            double rw = tw - lw;
            if (rw <= 0) break;
            double r1r = t1r - l1r, r1g = t1g - l1g, r1b = t1b - l1b;
            double sse = (l2 - (l1r * l1r + l1g * l1g + l1b * l1b) / lw) + ((t2 - l2) - (r1r * r1r + r1g * r1g + r1b * r1b) / rw);
            if (sse < best) { best = sse; cut = i + 1; }
        }
        Box lo = new Box(), hi = new Box();
        lo.Lo = b.Lo; lo.Hi = b.Lo + cut;
        hi.Lo = b.Lo + cut; hi.Hi = b.Hi;
        Measure(lo); Measure(hi);
        return new Box[] { lo, hi };
    }

    static void WriteU16(Stream s, int v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)((v >> 8) & 0xFF)); }

    // Signature, logical screen = frame size, global table (256 entries), NETSCAPE2.0 loop block (forever).
    void WriteHeader(Stream s, int w, int h)
    {
        byte[] sig = System.Text.Encoding.ASCII.GetBytes("GIF89a");
        s.Write(sig, 0, 6);
        WriteU16(s, w); WriteU16(s, h);
        s.WriteByte(0xF7);                                       // global table present, 8 bits per channel, 256 entries
        s.WriteByte(0);                                          // background color index
        s.WriteByte(0);                                          // pixel aspect ratio
        s.Write(pal, 0, 768);
        byte[] app = System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0");
        s.WriteByte(0x21); s.WriteByte(0xFF); s.WriteByte(11);
        s.Write(app, 0, 11);
        s.WriteByte(3); s.WriteByte(1); WriteU16(s, 0); s.WriteByte(0);
    }

    // Graphic Control Extension: disposal 1 (leave in place), delay in centiseconds, transparent index 255.
    static void WriteGce(Stream s, int delayCs, bool transparent)
    {
        s.WriteByte(0x21); s.WriteByte(0xF9); s.WriteByte(4);
        s.WriteByte((byte)(transparent ? 0x05 : 0x04));
        WriteU16(s, delayCs);
        s.WriteByte(Transparent);
        s.WriteByte(0);
    }

    static void WriteDescriptor(Stream s, int x, int y, int w, int h)
    {
        s.WriteByte(0x2C);
        WriteU16(s, x); WriteU16(s, y); WriteU16(s, w); WriteU16(s, h);
        s.WriteByte(0);                                          // no local table, not interlaced
    }
}
'@
}

$script:ExitCode = 1
$script:Fallbacks = 0
$script:BlackStreak = 0             # consecutive frames where PrintWindow gave nothing usable
$script:LogPos = 0L
$script:LogWarned = $false
$script:Finish = ''                 # 'done' or 'step threw' once the log reports it
$script:Marks = @{}                 # marker text -> time of its last log line
$script:MarkRx = [regex]'(?m)^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}).*?\[PROBE\] (cut1 start|cut1 end|cut2 start|cut3 end|done|step threw)'

function Say([string]$Message) { Write-Host ('[{0:HH:mm:ss}] {1}' -f (Get-Date), $Message) }

# .NET file calls resolve relative paths against the process directory, not the PowerShell location.
function Get-FullPath([string]$Path) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path) }

function Wait-GameWindow {
    param([int]$TimeoutSec)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $found = Get-Process -Name RimWorld -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($found -or (Get-Date) -ge $deadline) { return $found }
        Start-Sleep -Seconds 1
    }
}

# Only frame files are removed, so a folder shared with other files stays intact.
function Reset-FramesDir {
    [void](New-Item -ItemType Directory -Force -Path $FramesDir)
    Get-ChildItem -LiteralPath $FramesDir -Filter 'frame_*.png' -File | Remove-Item -Force
}

# True when 9 points spread over the bitmap are all nearly black, which is what an empty PrintWindow surface looks like.
function Test-Black([Drawing.Bitmap]$Bmp) {
    foreach ($fy in 1, 3, 5) {
        foreach ($fx in 1, 3, 5) {
            $c = $Bmp.GetPixel([int][Math]::Floor($Bmp.Width * $fx / 6), [int][Math]::Floor($Bmp.Height * $fy / 6))
            if ($c.R -ge 8 -or $c.G -ge 8 -or $c.B -ge 8) { return $false }
        }
    }
    return $true
}

# Client area of the window as a new bitmap, or $null when the window is minimized or gone. PrintWindow fills a bitmap
# of the whole window (PW_RENDERFULLCONTENT = 2) that is cropped to the client area; if that comes back empty or black
# the same rectangle is copied from the screen instead. After 5 black results in a row PrintWindow is only retried every
# 50th frame, so a game that never shows up in it does not pay for two grabs per frame.
function Get-ClientBitmap {
    param([IntPtr]$Hwnd)
    $geo = [GifCapNative]::Geometry($Hwnd)
    if (-not $geo) { return $null }
    if ($script:BlackStreak -lt 5 -or $script:BlackStreak % 50 -eq 0) {
        $full = [Drawing.Bitmap]::new($geo[6], $geo[7], [Drawing.Imaging.PixelFormat]::Format32bppRgb)
        try {
            $gfx = [Drawing.Graphics]::FromImage($full)
            try {
                $hdc = $gfx.GetHdc()
                try { $printed = [GifCapNative]::PrintWindow($Hwnd, $hdc, 2) } finally { $gfx.ReleaseHdc($hdc) }
            } finally { $gfx.Dispose() }
            $client = $full.Clone([Drawing.Rectangle]::new($geo[4], $geo[5], $geo[2], $geo[3]), $full.PixelFormat)
        } finally { $full.Dispose() }
        if ($printed -and -not (Test-Black $client)) { $script:BlackStreak = 0; return $client }
        $client.Dispose()
    }
    $script:BlackStreak++
    $script:Fallbacks++
    $screen = [Drawing.Bitmap]::new($geo[2], $geo[3], [Drawing.Imaging.PixelFormat]::Format32bppRgb)
    $gfx = [Drawing.Graphics]::FromImage($screen)
    try { $gfx.CopyFromScreen($geo[0], $geo[1], 0, 0, [Drawing.Size]::new($geo[2], $geo[3])) } finally { $gfx.Dispose() }
    return $screen
}

# Reads what the game appended to the log since the last call and notes the time of each [PROBE] marker. Lines stamped
# before the game process started are leftovers of an earlier run and are ignored. Returns $true once the run is over.
function Read-GameLog {
    param([datetime]$GameStart)
    try { $fs = [IO.File]::Open($Log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete') }
    catch [IO.IOException], [UnauthorizedAccessException] {              # not created yet, or being recreated: try again next time
        if (-not $script:LogWarned) { $script:LogWarned = $true; Say "Log not readable yet: $($_.Exception.Message)" }
        return [bool]$script:Finish
    }
    try {
        if ($fs.Length -lt $script:LogPos) { $script:LogPos = 0L }       # the game restarted the log
        $buf = [byte[]]::new([int]($fs.Length - $script:LogPos))
        [void]$fs.Seek($script:LogPos, [IO.SeekOrigin]::Begin)
        $got = 0
        while ($got -lt $buf.Length) {
            $n = $fs.Read($buf, $got, $buf.Length - $got)
            if ($n -le 0) { break }
            $got += $n
        }
    } finally { $fs.Dispose() }
    $last = if ($got -gt 0) { [Array]::LastIndexOf($buf, [byte]10, $got - 1) } else { -1 }
    if ($last -lt 0) { return [bool]$script:Finish }                      # no complete new line yet
    $script:LogPos += $last + 1                                           # the game may be mid-line: keep whole lines only
    $text = [Text.Encoding]::UTF8.GetString($buf, 0, $last + 1)
    foreach ($m in $script:MarkRx.Matches($text)) {
        $time = [datetime]::ParseExact($m.Groups[1].Value, 'yyyy-MM-dd HH:mm:ss.fff', [Globalization.CultureInfo]::InvariantCulture)
        if ($time -lt $GameStart.AddSeconds(-2)) { continue }
        $name = $m.Groups[2].Value
        if ($name -eq 'done' -or $name -eq 'step threw') { if (-not $script:Finish) { $script:Finish = $name } }
        else { $script:Marks[$name] = $time }
    }
    return [bool]$script:Finish
}

# Capture loop: frame k is aimed at k / Fps seconds after the start; a frame that runs late skips the slots it missed
# instead of bursting to catch up. Scaling and saving happen on the writer's thread.
function Invoke-Recording {
    param($Proc)
    $hwnd = $Proc.MainWindowHandle
    $gameStart = $Proc.StartTime
    $interval = 1000.0 / $Fps
    $saved = 0; $missed = 0; $slot = 0.0; $nextLogCheck = 0.0; $why = ''; $lastError = ''
    $script:Fallbacks = 0
    $script:BlackStreak = 0
    $writer = [GifCapWriter]::new()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    [void][GifCapNative]::timeBeginPeriod(1)                              # 1 ms sleep granularity instead of ~15 ms
    try {
        while ($true) {
            $ahead = $slot * $interval - $clock.Elapsed.TotalMilliseconds
            if ($ahead -ge 1) { [Threading.Thread]::Sleep([int][Math]::Floor($ahead)) }
            $now = $clock.Elapsed.TotalMilliseconds
            if ($now -ge $MaxSeconds * 1000) { $why = "-MaxSeconds $MaxSeconds reached"; break }
            if ($Proc.HasExited) { $why = 'game process exited'; break }
            if ($now -ge $nextLogCheck) {
                $nextLogCheck = $now + 1000
                if ($writer.LastError -and $writer.LastError -ne $lastError) { $lastError = $writer.LastError; Say "Frame not saved: $lastError" }
                if (Read-GameLog $gameStart) { $why = "log reports [PROBE] $script:Finish"; break }
            }
            $shot = $null
            try {
                $ticks = [DateTime]::Now.Ticks                            # taken before the grab, so the picture is never older than its stamp
                if ($hwnd -ne [IntPtr]::Zero) { $shot = Get-ClientBitmap $hwnd }
                if ($shot) {
                    $height = [Math]::Max(2, 2 * [int][Math]::Round([double]$Width * $shot.Height / $shot.Width / 2))
                    $writer.Add($shot, $Width, $height, (Join-Path $FramesDir ('frame_{0:000000}_{1}.png' -f $saved, $ticks)))
                    $shot = $null                                         # the writer owns it now
                    $saved++
                } else {
                    $missed++
                    $Proc.Refresh(); $hwnd = $Proc.MainWindowHandle       # the window may have been recreated
                }
            } catch {
                $missed++
                if ($_.Exception.Message -ne $lastError) { $lastError = $_.Exception.Message; Say "Frame skipped: $lastError" }
            } finally { if ($shot) { $shot.Dispose() } }
            $slot = [Math]::Max($slot + 1, [Math]::Ceiling($clock.Elapsed.TotalMilliseconds / $interval))
        }
    } finally {
        [void][GifCapNative]::timeEndPeriod(1)
        $secs = $clock.Elapsed.TotalSeconds
        $writer.Finish()                                                  # waits for the frames still queued
    }
    $saved -= $writer.Failed
    if ($writer.Failed) { Say ('{0} frame(s) could not be saved: {1}' -f $writer.Failed, $writer.LastError) }
    [void](Read-GameLog $gameStart)                                       # lines written since the last check
    Say ('Captured {0} frames in {1:N1} s = {2:N1} fps (target {3}); {4} used the screen-copy fallback, {5} missed; stopped: {6}' -f
        $saved, $secs, ($saved / $secs), $Fps, $script:Fallbacks, $missed, $why)
    if ($saved -gt 1 -and $saved / $secs -lt 0.9 * $Fps) {
        Say ('WARNING: capture stayed below the target rate, so the GIF will play about {0:N0}% too fast; lower -Fps or -Width' -f (100 * ($Fps * $secs / $saved - 1)))
    }
}

# Frames to keep, in capture order: cut1 start..cut1 end plus cut2 start..cut3 end. With a marker missing (or the
# markers out of order) everything from the first available start marker to the end of the recording is kept.
# Cut 1 opens 150 ms after its marker: the camera move behind that log line needs that long to be rendered and
# presented, and a frame stamped sooner can still show the view from before it.
function Select-Frames {
    $all = @(Get-ChildItem -LiteralPath $FramesDir -Filter 'frame_*.png' -File | ForEach-Object {
        if ($_.Name -match '^frame_(\d+)_(\d+)\.png$') { [pscustomobject]@{ Index = [int64]$Matches[1]; Ticks = [int64]$Matches[2]; Path = $_.FullName } }
    } | Sort-Object Index)
    $m = $script:Marks
    $missing = @('cut1 start', 'cut1 end', 'cut2 start', 'cut3 end' | Where-Object { -not $m.ContainsKey($_) })
    if ($missing.Count -eq 0 -and $m['cut1 start'] -le $m['cut1 end'] -and $m['cut2 start'] -le $m['cut3 end']) {
        $ranges = @(@($m['cut1 start'].AddMilliseconds(150).Ticks, $m['cut1 end'].Ticks), @($m['cut2 start'].Ticks, $m['cut3 end'].Ticks))
    } else {
        if ($missing.Count) { Say ('Log marker missing: ' + ($missing -join ', ')) } else { Say 'Log markers are out of order' }
        $starts = @('cut1 start', 'cut2 start' | Where-Object { $m.ContainsKey($_) } | ForEach-Object { $m[$_].Ticks })
        if ($starts.Count) { $from = [int64]($starts | Sort-Object | Select-Object -First 1) } else { $from = 0L; Say 'No start marker either; keeping the whole recording' }
        $ranges = @(, @($from, [int64]::MaxValue))
    }
    $kept = foreach ($f in $all) {
        foreach ($r in $ranges) { if ($f.Ticks -ge $r[0] -and $f.Ticks -le $r[1]) { $f; break } }
    }
    Say ('Keeping {0} of {1} captured frames' -f @($kept).Count, $all.Count)
    return @($kept)
}

# Encodes the frames (GifEncoder) and writes the file. GifWidth 0 keeps the width of the frames.
function Write-Gif {
    param([string[]]$Paths, [string]$Dest, [double]$Fps, [int]$GifWidth, [bool]$UseDither, [int]$Calm = 0)
    $delay = [int][Math]::Round(100 / $Fps, [MidpointRounding]::AwayFromZero)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $res = [GifEncoder]::Encode($Paths, $GifWidth, $delay, $UseDither, $Calm, [Action[string]]{ param($Text) Say $Text })
    if ($res.Skipped) { Say "Left out $($res.Skipped) frame(s) whose size differs from the first one (window was resized)" }
    if ($res.Unreadable) { Say "Left out $($res.Unreadable) frame(s) that could not be read (first: $($res.FirstError))" }
    [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Dest))
    [IO.File]::WriteAllBytes($Dest, $res.Data)
    $size = $res.Data.Length
    Say ('Wrote {0}: {1:N0} bytes ({2:N0} KB), {3} frames, {4}x{5}, {6} cs per frame, loops forever' -f
        $Dest, $size, ($size / 1KB), $res.Frames, $res.Width, $res.Height, $delay)
    Say ('{0:N1} KB per frame; palette {1} colors + 1 transparent index (256-entry global table); dither {2}, tolerance {3}; encoded in {4:N1} s' -f
        ($size / 1KB / $res.Frames), $res.Colors, $(if ($UseDither) { 'on' } else { 'off' }), $Calm, $clock.Elapsed.TotalSeconds)
    Say ('{0} of {1} frames differenced ({2} identical to the one before); the changed rectangle averages {3:N1}% of the frame, the changed pixels {4:N1}%' -f
        ($res.Frames - 1), $res.Frames, $res.Unchanged, $res.AvgRectPercent, $res.AvgChangedPercent)
}

function Invoke-Main {
    $script:ExitCode = 1
    try {
        $script:OutGif = Get-FullPath $OutGif
        $script:FramesDir = Get-FullPath $FramesDir
        $script:Log = Get-FullPath $Log
        if ($EncodeOnly) {
            if (-not (Test-Path -LiteralPath $Log -PathType Leaf)) { throw "Log file not found: $Log" }
            if (-not (Test-Path -LiteralPath $FramesDir -PathType Container)) { throw "Frames folder not found: $FramesDir" }
            [void](Read-GameLog ([datetime]'2000-01-01'))                 # the whole log: there is no game start to compare with
        } else {
            [void][GifCapNative]::SetProcessDPIAware()                   # real pixel sizes on scaled displays
            Say "Waiting up to $WindowWaitSeconds s for the RimWorld window"
            $proc = Wait-GameWindow $WindowWaitSeconds
            if (-not $proc) { Say "No RimWorld window appeared within $WindowWaitSeconds s"; $script:ExitCode = 2; return }
            Say ('Found RimWorld: pid {0}, window 0x{1:X}' -f $proc.Id, $proc.MainWindowHandle.ToInt64())
            Reset-FramesDir
            Invoke-Recording $proc
        }
        $kept = @(Select-Frames)
        if ($kept.Count -eq 0) { Say 'No frame falls inside the cut ranges; the captured frames stay in the frames folder'; $script:ExitCode = 3; return }
        $gifWidth = if ($EncodeOnly -and -not $script:WidthGiven) { 0 } else { $Width }
        Write-Gif -Paths @($kept | ForEach-Object { $_.Path }) -Dest $OutGif -Fps $Fps -GifWidth $gifWidth -UseDither $Dither.IsPresent -Calm $Tolerance
        $script:ExitCode = 0
    } catch {
        Say ('FAILED at line {0}: {1}' -f $_.InvocationInfo.ScriptLineNumber, $_.Exception.Message)
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-Main
    exit $script:ExitCode
}
