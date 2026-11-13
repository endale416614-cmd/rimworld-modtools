"""Static check of the live mod list.

usage: py -I listcheck.py ModsConfig.xml OUTDIR [OLDER_ModsConfig.xml]

Resolves every active id to the folder the game loads (an id ending in _steam is the workshop copy, a plain id is
the local Mods copy when one exists), then writes to OUTDIR:
  resolved.tsv    position, id, source, folder, name, versions, dll count
  about.txt       missing mods, missing dependencies, declared incompatibilities, load order violations,
                  mods with no 1.7 tag that ship DLLs, both copies active, list diff against the older config
  assemblies.tsv  DLL file names shipped by more than one active mod, with size and hash per copy
  defs.tsv        (def type, defName) defined by more than one active mod, in load order
  parents.tsv     abstract Name= values defined by more than one active mod
  patches.tsv     per mod: patch files, operations, xpaths that start with // or use //*
  textures.tsv    per mod: texture files, bytes on disk, estimated uncompressed megabytes, images over 2048 px
  texcollisions.tsv  texture paths that more than one active mod ships, grouped by the mods involved
  xmlerrors.txt   XML files that do not parse
Reads only. Version rule for load folders: LoadFolders.xml entry v1.7, else the newest older entry; without that
file the mod root, Common and the newest version folder up to 1.7.
"""
import hashlib, os, re, struct, sys
import xml.etree.ElementTree as ET
from collections import defaultdict

GAME = r"F:\Steam\steamapps\common\RimWorld"
WORKSHOP = r"F:\Steam\steamapps\workshop\content\294100"
TARGET = (1, 7)
VER = re.compile(r"^v?(\d+)\.(\d+)$")


def norm(i):
    i = i.strip().lower()
    return i[:-6] if i.endswith("_steam") else i


def text(e, tag):
    x = e.find(tag)
    return (x.text or "").strip() if x is not None else ""


def items(e, tag):
    """Base list plus the newest per-version list up to the target version."""
    out = [(c.text or "").strip().lower() for c in e.find(tag)] if e.find(tag) is not None else []
    by = e.find(tag + "ByVersion")
    if by is not None:
        best = None
        for v in by:
            m = VER.match(v.tag)
            if m and (int(m[1]), int(m[2])) <= TARGET and (best is None or (int(m[1]), int(m[2])) > best[0]):
                best = ((int(m[1]), int(m[2])), v)
        if best:
            out += [(c.text or "").strip().lower() for c in best[1]]
    return [x for x in out if x]


def deps(e):
    out = []
    for tag in ("modDependencies",):
        for src in ([e.find(tag)] if e.find(tag) is not None else []):
            out += [text(c, "packageId").lower() for c in src]
        by = e.find(tag + "ByVersion")
        if by is not None:
            best = None
            for v in by:
                m = VER.match(v.tag)
                if m and (int(m[1]), int(m[2])) <= TARGET and (best is None or (int(m[1]), int(m[2])) > best[0]):
                    best = ((int(m[1]), int(m[2])), v)
            if best:
                out += [text(c, "packageId").lower() for c in best[1]]
    return [x for x in out if x]


def index(root):
    idx = {}
    if not os.path.isdir(root):
        return idx
    for d in os.listdir(root):
        p = os.path.join(root, d, "About", "About.xml")
        if not os.path.isfile(p):
            continue
        try:
            e = ET.parse(p).getroot()
        except Exception as ex:
            print("BAD About.xml", p, ex, file=sys.stderr)
            continue
        pid = text(e, "packageId").lower()
        if not pid:
            continue
        sv = e.find("supportedVersions")
        idx[pid] = dict(
            dir=os.path.join(root, d), name=text(e, "name"),
            vers=[(c.text or "").strip() for c in sv] if sv is not None else [],
            deps=deps(e), incompat=items(e, "incompatibleWith"),
            after=items(e, "loadAfter") + items(e, "forceLoadAfter"),
            before=items(e, "loadBefore") + items(e, "forceLoadBefore"))
    return idx


def load_folders(mod, active):
    lf = next((os.path.join(mod, f) for f in os.listdir(mod) if f.lower() == "loadfolders.xml"), None)
    if lf:
        try:
            root = ET.parse(lf).getroot()
        except Exception:
            root = None
        if root is not None:
            best = None
            for v in root:
                m = VER.match(v.tag)
                if m and (int(m[1]), int(m[2])) <= TARGET and (best is None or (int(m[1]), int(m[2])) > best[0]):
                    best = ((int(m[1]), int(m[2])), v)
            if best is None:
                best = (None, root.find("default"))
            out = []
            for li in (best[1] if best[1] is not None else []):
                need = [x.strip().lower() for x in (li.get("IfModActive") or "").split(",") if x.strip()]
                deny = [x.strip().lower() for x in (li.get("IfModNotActive") or "").split(",") if x.strip()]
                if need and not any(n in active for n in need):
                    continue
                if any(n in active for n in deny):
                    continue
                rel = (li.text or "").strip().strip("/\\")
                path = os.path.join(mod, rel) if rel else mod
                if os.path.isdir(path) and path not in out:
                    out.append(path)
            return out
    out = [mod]
    if os.path.isdir(os.path.join(mod, "Common")):
        out.append(os.path.join(mod, "Common"))
    vers = sorted(((int(m[1]), int(m[2])), d) for d in os.listdir(mod) for m in [VER.match(d)]
                  if m and (int(m[1]), int(m[2])) <= TARGET and os.path.isdir(os.path.join(mod, d)))
    if vers:
        out.append(os.path.join(mod, vers[-1][1]))
    return out


def image_size(path):
    try:
        with open(path, "rb") as f:
            h = f.read(32)
        if h[:8] == b"\x89PNG\r\n\x1a\n":
            return struct.unpack(">II", h[16:24])
        if h[:4] == b"DDS ":
            hgt, wid = struct.unpack("<II", h[12:20])
            return wid, hgt
    except OSError:
        pass
    return None


def main():
    cfg, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    active = [(li.text or "").strip().lower() for li in ET.parse(cfg).getroot().find("activeMods")]
    ws, local, data = index(WORKSHOP), index(os.path.join(GAME, "Mods")), index(os.path.join(GAME, "Data"))
    mods, missing = [], []
    for raw in active:
        n = norm(raw)
        if raw.endswith("_steam"):
            rec, src = ws.get(n), "workshop"
        else:
            rec, src = (local.get(n), "local") if n in local else (data.get(n), "data") if n in data else (ws.get(n), "workshop")
        if rec is None:
            missing.append(raw)
            continue
        mods.append(dict(rec, raw=raw, id=n, src=src, pos=len(mods)))
    aset = {m["id"] for m in mods}
    pos = {m["id"]: m["pos"] for m in mods}

    about = open(os.path.join(out, "about.txt"), "w", encoding="utf-8")
    def section(title, rows):
        about.write(f"== {title} ({len(rows)})\n" + "".join(r + "\n" for r in rows) + "\n")
    section("ACTIVE ID WITH NO INSTALLED FOLDER", missing)
    seen = defaultdict(list)
    for m in mods:
        seen[m["id"]].append(m["raw"])
    section("BOTH COPIES ACTIVE", [f"{k}: {v}" for k, v in seen.items() if len(v) > 1])
    known = set(ws) | set(local) | set(data)
    section("MISSING DEPENDENCY", [f"{m['raw']} ({m['name']}) needs {d} [{'installed, inactive' if d in known else 'not installed'}]"
                                   for m in mods for d in m["deps"] if d not in aset])
    section("DECLARED INCOMPATIBLE, BOTH ACTIVE", [f"{m['raw']} ({m['name']}) <-> {x}" for m in mods for x in m["incompat"] if x in aset])
    order = []
    for m in mods:
        order += [f"{m['raw']} @{m['pos']} must load AFTER {x} @{pos[x]}" for x in m["after"] + m["deps"] if x in aset and pos[x] > m["pos"]]
        order += [f"{m['raw']} @{m['pos']} must load BEFORE {x} @{pos[x]}" for x in m["before"] if x in aset and pos[x] < m["pos"]]
    section("LOAD ORDER VIOLATIONS", sorted(set(order)))
    names = defaultdict(list)
    for m in mods:
        names[re.sub(r"\W+|continued|unofficial|temp|fork", "", m["name"].lower())].append(m["raw"])
    section("SAME DISPLAY NAME, DIFFERENT IDS", [f"{k}: {v}" for k, v in names.items() if len(v) > 1 and k])

    dlls = defaultdict(list)
    defs, parents, texpaths = defaultdict(list), defaultdict(list), defaultdict(list)
    patches, textures, xmlerr, resolved, old = [], [], [], [], []
    for m in mods:
        folders = load_folders(m["dir"], aset)
        ndll = 0
        seen_dll = set()
        for folder in reversed(folders):
            asm = os.path.join(folder, "Assemblies")
            for base, _, files in os.walk(asm):
                for f in files:
                    if f.lower().endswith(".dll") and f.lower() not in seen_dll:
                        seen_dll.add(f.lower())
                        p = os.path.join(base, f)
                        dlls[f.lower()].append((m, p))
                        ndll += 1
        resolved.append("\t".join([str(m["pos"]), m["raw"], m["src"], m["dir"], m["name"], ",".join(m["vers"]), str(ndll)]))
        if ndll and m["src"] != "data" and not any(v.startswith("1.7") for v in m["vers"]):
            old.append(f"{m['raw']} ({m['name']}) versions {','.join(m['vers'])} dlls {ndll}")
        nfile = nop = slow = wild = 0
        for folder in folders:
            for kind in ("Defs", "Patches"):
                for base, _, files in os.walk(os.path.join(folder, kind)):
                    for f in files:
                        if not f.lower().endswith(".xml"):
                            continue
                        p = os.path.join(base, f)
                        try:
                            root = ET.parse(p).getroot()
                        except Exception:
                            # Comments in another code page are not valid UTF-8. The game reads such bytes as
                            # replacement characters and loads the file, so do the same before calling it broken.
                            try:
                                root = ET.fromstring(open(p, "rb").read().decode("utf-8", "replace").lstrip("﻿"))
                            except Exception as ex:
                                xmlerr.append(f"{m['raw']}\t{p}\t{ex}")
                                continue
                        if kind == "Defs":
                            for d in root:
                                if not isinstance(d.tag, str):
                                    continue
                                dn = text(d, "defName")
                                if dn:
                                    defs[(d.tag, dn)].append(m)
                                elif d.get("Name"):
                                    parents[(d.tag, d.get("Name"))].append(m)
                        else:
                            nfile += 1
                            for x in root.iter("xpath"):
                                nop += 1
                                xp = (x.text or "").strip()
                                slow += xp.startswith("//") or xp.startswith("/Defs//") or xp.startswith("Defs//")
                                wild += "//*" in xp or "/*[" in xp
        if nfile:
            patches.append((nop, f"{m['raw']}\t{m['name']}\t{nfile}\t{nop}\t{slow}\t{wild}"))
        cnt = size = big = 0
        est = 0.0
        for folder in folders:
            for base, _, files in os.walk(os.path.join(folder, "Textures")):
                names_here = {f.lower() for f in files}
                for f in files:
                    low = f.lower()
                    if not low.endswith((".png", ".dds", ".jpg", ".jpeg", ".psd")):
                        continue
                    # The game prefers a .dds twin over the .png next to it, so count the pair once.
                    if low.endswith(".png") and low[:-4] + ".dds" in names_here:
                        continue
                    p = os.path.join(base, f)
                    texpaths[os.path.splitext(os.path.relpath(p, os.path.join(folder, "Textures")).lower())[0]].append(m)
                    cnt += 1
                    size += os.path.getsize(p)
                    wh = image_size(p)
                    if wh:
                        est += wh[0] * wh[1] * (1 if low.endswith(".dds") else 4) * 1.333 / 1048576
                        big += max(wh) > 2048
        if cnt:
            textures.append((est, f"{m['raw']}\t{m['name']}\t{cnt}\t{size / 1048576:.1f}\t{est:.0f}\t{big}"))
    section("NO 1.7 TAG AND SHIPS DLLS", old)

    if len(sys.argv) > 3:
        prev = [(li.text or "").strip().lower() for li in ET.parse(sys.argv[3]).getroot().find("activeMods")]
        section(f"ADDED SINCE {os.path.basename(sys.argv[3])}", [a for a in active if a not in prev])
        section(f"REMOVED SINCE {os.path.basename(sys.argv[3])}", [a for a in prev if a not in active])
    about.close()

    def write(name, head, rows):
        with open(os.path.join(out, name), "w", encoding="utf-8") as f:
            f.write(head + "\n" + "".join(r + "\n" for r in rows))
    write("resolved.tsv", "pos\tid\tsource\tfolder\tname\tversions\tdlls", resolved)
    rows = []
    for name, copies in sorted(dlls.items()):
        if len(copies) > 1:
            sigs = [(m["pos"], m["raw"], os.path.getsize(p), hashlib.sha1(open(p, "rb").read()).hexdigest()[:10]) for m, p in copies]
            same = len({s[3] for s in sigs}) == 1
            rows += [f"{name}\t{'same' if same else 'DIFFERENT'}\t{s[0]}\t{s[1]}\t{s[2]}\t{s[3]}" for s in sigs]
    write("assemblies.tsv", "dll\tcopies\tpos\tmod\tbytes\tsha1", rows)
    for name, table in (("defs.tsv", defs), ("parents.tsv", parents)):
        rows = [f"{k[0]}\t{k[1]}\t{len({m['id'] for m in v})}\t" + " > ".join(dict.fromkeys(m["raw"] for m in v))
                for k, v in sorted(table.items()) if len({m["id"] for m in v}) > 1]
        write(name, "type\tname\tmods\tload order (last wins)", rows)
    # One texture path in two mods: the later mod's file is the one drawn for both.
    pairs = defaultdict(list)
    for rel, owners in texpaths.items():
        ids = list(dict.fromkeys(m["raw"] for m in owners))
        if len(ids) > 1:
            pairs[" > ".join(ids)].append(rel)
    write("texcollisions.tsv", "mods in load order (last wins)\tpaths\texamples",
          [f"{k}\t{len(v)}\t{', '.join(sorted(v)[:4])}" for k, v in sorted(pairs.items(), key=lambda kv: -len(kv[1]))])
    write("patches.tsv", "mod\tname\tfiles\txpaths\tstart_with_//\twildcard", [r for _, r in sorted(patches, reverse=True)])
    write("textures.tsv", "mod\tname\tfiles\tdisk_mb\test_vram_mb\tover_2048", [r for _, r in sorted(textures, reverse=True)])
    with open(os.path.join(out, "xmlerrors.txt"), "w", encoding="utf-8") as f:
        f.write("".join(r + "\n" for r in xmlerr))
    print(f"active {len(active)} resolved {len(mods)} missing {len(missing)} dll names shared {sum(1 for v in dlls.values() if len(v) > 1)} "
          f"def collisions {sum(1 for v in defs.values() if len({m['id'] for m in v}) > 1)} xml errors {len(xmlerr)}")


if __name__ == "__main__":
    main()
