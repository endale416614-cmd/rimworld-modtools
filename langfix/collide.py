r"""Check whether any mod that loads after LocalPack Hotfixes can undo its text overrides.

usage: py -I collide.py [ModsConfig.xml]

The overrides only work because the game stores keyed strings and def injections with SetOrAdd, so the last mod
loaded wins. LocalPack Hotfixes is NOT last: on the 2026-11-17 list it is entry 451 of 555, with 104 mods after
it. That is fine as long as none of those 104 writes the same key, the same def injection path, or patches the
same def field. This checks all three and names every collision; a clean run is what makes the overrides safe.

Reads only. Run again whenever the mod list changes.
"""
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "audit"))
import listcheck as lc

HOTFIX_ID = "local.lp17.hotfix"
INSTALL = r"F:\Steam\steamapps\common\RimWorld\Mods\16-compat__local.lp17.hotfix"
CONFIG = os.path.expandvars(
    r"%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml")


def parse(path):
    try:
        return ET.parse(path).getroot()
    except Exception:
        return None


def ours():
    """The keys, injection paths and patched def names the hotfix ships."""
    keys, inject, patched = set(), set(), set()
    base = os.path.join(INSTALL, "1.7", "Languages", "English")
    kp = os.path.join(base, "Keyed", "LP17Text.xml")
    root = parse(kp)
    if root is not None:
        keys = {c.tag for c in root if isinstance(c.tag, str)}
    di = os.path.join(base, "DefInjected")
    if os.path.isdir(di):
        for d in sorted(os.listdir(di)):
            root = parse(os.path.join(di, d, "LP17Text.xml"))
            if root is not None:
                inject |= {(d, c.tag) for c in root if isinstance(c.tag, str)}
    root = parse(os.path.join(INSTALL, "1.7", "Patches", "LP17Text.xml"))
    if root is not None:
        for op in root:
            xp = op.findtext("xpath") or ""
            for m in re.finditer(r'\[@?(?:defName|Name)="([^"]+)"\]', xp):
                patched.add(m.group(1))
    return keys, inject, patched


def theirs(folders, lang="English"):
    """Keyed keys, (defType, path) injections and patch xpaths one mod contributes."""
    keys, inject, xpaths = set(), set(), []
    for folder in folders:
        for dirpath, dirnames, files in os.walk(folder):
            low = (dirpath + os.sep).replace("\\", "/").lower()
            if "/languages/" in low:
                after = low.split("/languages/", 1)[1].split("/", 1)[0]
                if after and after != lang.lower():
                    dirnames[:] = []
                    continue
            for f in files:
                if not f.lower().endswith(".xml"):
                    continue
                path = os.path.join(dirpath, f)
                root = parse(path)
                if root is None:
                    continue
                if "/definjected/" in low:
                    inject |= {(os.path.basename(dirpath), c.tag) for c in root if isinstance(c.tag, str)}
                elif "/keyed/" in low:
                    keys |= {c.tag for c in root if isinstance(c.tag, str)}
                elif root.tag == "Patch":
                    for op in root.iter():
                        xp = op.findtext("xpath")
                        if xp:
                            xpaths.append((path, xp.strip()))
    return keys, inject, xpaths


def main():
    cfg = sys.argv[1] if len(sys.argv) > 1 else CONFIG
    active = [(li.text or "").strip().lower() for li in ET.parse(cfg).getroot().find("activeMods")]
    if HOTFIX_ID not in active:
        print(HOTFIX_ID, "is not in the active list")
        return
    after = active[active.index(HOTFIX_ID) + 1:]
    keys, inject, patched = ours()
    print(f"hotfix ships {len(keys)} keyed, {len(inject)} injections, {len(patched)} patched defs; "
          f"{len(after)} mods load after it")

    ws, local, data = lc.index(lc.WORKSHOP), lc.index(os.path.join(lc.GAME, "Mods")), lc.index(
        os.path.join(lc.GAME, "Data"))
    normset = {lc.norm(i) for i in active}
    hits = defaultdict(list)
    for raw in after:
        pid = lc.norm(raw)
        if pid in data:
            continue
        src = ws if raw.endswith("_steam") else local
        mod = src.get(pid) or (local.get(pid) if raw.endswith("_steam") else ws.get(pid))
        if not mod:
            print("  unresolved", raw)
            continue
        tk, ti, tx = theirs(lc.load_folders(mod["dir"], normset))
        for k in sorted(tk & keys):
            hits[pid].append(f"keyed {k}")
        for d, p in sorted(ti & inject):
            hits[pid].append(f"injection {d}/{p}")
        for path, xp in tx:
            for name in re.findall(r'\[@?(?:defName|Name)="([^"]+)"\]', xp):
                if name in patched:
                    hits[pid].append(f"patches {name}: {xp}  ({os.path.basename(path)})")

    if not hits:
        print("no collisions: nothing loading after the hotfix writes the same key, path or def")
        return
    print(f"COLLISIONS in {len(hits)} mods")
    for pid, rows in sorted(hits.items()):
        print(f"  {pid}")
        for r in rows:
            print(f"    {r}")


if __name__ == "__main__":
    main()
