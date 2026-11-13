"""Finds compressible ThingDefs whose natural short hash sits next to a given def's, to explain
"Tried loading non-compressible thing as compressed thing: <def>" after a mod list change.

usage: py -I shorthash.py resolved.tsv DEFNAME [WINDOW]

The game gives each def the short hash StableStringHash(defName) % 65535 and steps upward while that value is
taken, in def order. A new def that claims a value first pushes a later def up by one, and a saved map still holds
the old value. This lists the compressible ThingDefs (saveCompressible, inherited through ParentName) within WINDOW
of DEFNAME's natural hash. resolved.tsv comes from listcheck.py. Reads only.
"""
import csv, os, sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import listcheck


def natural(name):
    h = 23
    for ch in name:
        h = (h * 31 + ord(ch)) & 0xFFFFFFFF
    if h >= 0x80000000:
        h -= 0x100000000
    r = abs(h) % 65535  # C# remainder keeps the sign of the dividend
    return (-r if h < 0 else r) & 0xFFFF


def main():
    rows = list(csv.DictReader(open(sys.argv[1], encoding="utf-8"), delimiter="\t"))
    target = sys.argv[2]
    window = int(sys.argv[3]) if len(sys.argv) > 3 else 6
    active = {listcheck.norm(r["id"]) for r in rows}
    nodes, named = [], {}
    for r in rows:
        for folder in listcheck.load_folders(r["folder"], active):
            for base, _, files in os.walk(os.path.join(folder, "Defs")):
                for f in files:
                    if not f.lower().endswith(".xml"):
                        continue
                    try:
                        root = ET.fromstring(open(os.path.join(base, f), "rb").read().decode("utf-8", "replace").lstrip("﻿"))
                    except Exception:
                        continue
                    for d in root:
                        if isinstance(d.tag, str) and d.tag == "ThingDef":
                            if d.get("Name"):
                                named[d.get("Name")] = d
                            nodes.append((r["id"], d))

    def compressible(d, depth=0):
        x = d.find("saveCompressible")
        if x is not None:
            return (x.text or "").strip().lower() == "true"
        p = named.get(d.get("ParentName") or "")
        return compressible(p, depth + 1) if p is not None and depth < 30 else False

    want = natural(target)
    print(f"{target}: natural short hash {want}")
    hits = 0
    for mod, d in nodes:
        name = (d.findtext("defName") or "").strip()
        if name and compressible(d):
            hits += 1
            if abs(natural(name) - want) <= window:
                print(f"  {natural(name):6d} ({natural(name) - want:+d})  {name}  [{mod}]")
    print(f"compressible ThingDefs in XML: {hits}")


if __name__ == "__main__":
    main()
