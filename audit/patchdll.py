"""Maps each Harmony patch class in a probe patch table to the mod DLL that defines it.

usage: py -I patchdll.py PATCHTABLE.tsv resolved.tsv OUT.tsv

PATCHTABLE is the probe's static-mode table (column `patch` = Namespace.Type::Method), resolved.tsv comes from
listcheck.py. A DLL is a candidate when its metadata holds both the type's simple name and its namespace as strings.
Candidates are searched in the active mods' loaded Assemblies folders only; ties are listed with `;`.
"""
import csv, os, re, sys

TARGET = (1, 7)
VER = re.compile(r"^v?(\d+)\.(\d+)$")


def dll_files(mod):
    """DLLs of the newest version folder up to the target plus the mod root, first hit per file name."""
    folders = [mod]
    vers = sorted(((int(m[1]), int(m[2])), d) for d in os.listdir(mod) for m in [VER.match(d)]
                  if m and (int(m[1]), int(m[2])) <= TARGET and os.path.isdir(os.path.join(mod, d)))
    if vers:
        folders.insert(0, os.path.join(mod, vers[-1][1]))
    seen, out = set(), []
    for folder in folders:
        for base, _, files in os.walk(os.path.join(folder, "Assemblies")):
            for f in files:
                if f.lower().endswith(".dll") and f.lower() not in seen:
                    seen.add(f.lower())
                    out.append(os.path.join(base, f))
    return out


def main():
    table, resolved, out = sys.argv[1:4]
    classes = sorted({r["patch"].split("::")[0] for r in csv.DictReader(open(table, encoding="utf-8"), delimiter="\t")})
    blobs = []
    for r in csv.DictReader(open(resolved, encoding="utf-8"), delimiter="\t"):
        for dll in dll_files(r["folder"]):
            with open(dll, "rb") as f:
                blobs.append((r["id"], dll, f.read()))
    with open(out, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter="\t")
        w.writerow(["class", "mod", "dll"])
        missing = 0
        for c in classes:
            outer = c.split("+")[0]
            ns, _, name = outer.rpartition(".")
            need = [name.split("`")[0].encode()] + ([ns.encode()] if ns else [])
            hits = [(mod, dll) for mod, dll, data in blobs if all(n + b"\0" in data for n in need)]
            missing += not hits
            w.writerow([c, ";".join(dict.fromkeys(h[0] for h in hits)), ";".join(h[1] for h in hits)])
    print(f"classes {len(classes)} dlls {len(blobs)} unmapped {missing}")


if __name__ == "__main__":
    main()
