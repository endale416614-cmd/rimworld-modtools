r"""Turn the raw scan into review shards and drop what must not be touched.

usage: py -I prep.py SCANDIR

Reads SCANDIR\nonenglish.tsv and SCANDIR\typos.tsv, writes:
  review\nonenglish-NN.tsv  shards to translate, one mod per shard where it fits
  review\typos-NN.tsv       shards to judge
  ignored.tsv               rows deliberately not fixed, with the reason
  manual.tsv                rows that need a hand-written patch instead of a translation file
  prep.txt                  counts

Dropped, with reasons:
  other language   the file sits in a Languages/LanguageData folder for a language the game does not load
  not shown        the field is an asset path or sound name, not text the player reads
  custom field     the field is not one the vanilla game marks translatable, so a def injection would throw
                   InvalidOperationException and abort the rest of that def type's injections
  patch value      the string is inside a PatchOperation, so there is no def path to inject
Each kept row gets a stable id the reviewer echoes back, so a verdict can never land on the wrong string.
"""
import os
import re
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scan

# Fields the vanilla game marks [MustTranslate]; only these are safe to inject.
TRANSLATABLE = {
    "label", "labelshort", "labelplural", "labelmale", "labelfemale", "labelnoun",
    "description", "descriptionshort", "descriptionfuture", "deathmessage", "verb", "gerund",
    "gerundlabel", "jobstring", "reportstring", "text", "pawnlabel", "title", "titleshort",
    "titlefemale", "titleshortfemale", "letterlabel", "lettertext", "beginletter",
    "beginletterlabel", "summary", "ingestcommandstring", "ingestreportstring", "stuffadjective",
    "onmapinstruction", "li",
}
NOT_SHOWN = re.compile(r"path$|sound$|^texpath|^clippath|^castsound|^icon$|^compatibility$", re.I)
LANG_DIRS = {"languages", "languagedata"}
SHARD_NONENGLISH = 80
SHARD_TYPOS = 150


def rows(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        head = fh.readline().rstrip("\n").split("\t")
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == len(head):
                yield dict(zip(head, parts))


def other_language(relpath, lang="english"):
    parts = re.split(r"[\\/]+", relpath.lower())
    for i, p in enumerate(parts[:-1]):
        if p in LANG_DIRS and i + 1 < len(parts):
            return not parts[i + 1].startswith(lang)
    return False


def field_of(target):
    """Last named step of a def target path, skipping list indexes."""
    steps = target.split("/")[-1].split(".")
    for s in reversed(steps):
        if not s.isdigit():
            return s.lower()
    return ""


def write(path, rows_, cols):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as fh:
        fh.write("\t".join(cols) + "\n")
        for r in rows_:
            fh.write("\t".join(str(r.get(c, "")).replace("\t", " ") for c in cols) + "\n")


def classify(src, kinds):
    keep, ignored, manual, seen = [], [], [], set()
    for r in rows(src):
        sig = (r["kind"], r["target"], r["text"])
        if sig in seen:
            continue
        seen.add(sig)
        if other_language(r["file"]):
            ignored.append(dict(r, reason="other language"))
            continue
        field = field_of(r["target"])
        if r["kind"] == "def" and (NOT_SHOWN.search(field) or NOT_SHOWN.search(r["tag"])):
            ignored.append(dict(r, reason="not shown to the player"))
            continue
        if r["kind"] == "patch":
            manual.append(dict(r, reason="patch value, no def path"))
            continue
        if r["kind"] == "def" and field not in TRANSLATABLE:
            manual.append(dict(r, reason="custom field, injection would throw"))
            continue
        if r["kind"] not in kinds:
            ignored.append(dict(r, reason="kind " + r["kind"]))
            continue
        keep.append(r)
    return keep, ignored, manual


def shard(out, name, keep, cols, size, group_by_mod):
    os.makedirs(out, exist_ok=True)
    groups = defaultdict(list)
    for r in keep:
        groups[r["mod"] if group_by_mod else ""].append(r)
    batches, cur = [], []
    for mod in sorted(groups, key=lambda m: -len(groups[m])):
        items = groups[mod]
        if group_by_mod and len(items) >= size // 2:
            for i in range(0, len(items), size):
                batches.append(items[i:i + size])
            continue
        for r in items:
            cur.append(r)
            if len(cur) >= size:
                batches.append(cur)
                cur = []
    if cur:
        batches.append(cur)
    paths = []
    for i, b in enumerate(batches, 1):
        p = os.path.join(out, f"{name}-{i:02d}.tsv")
        write(p, b, cols)
        paths.append((p, len(b), sorted({r["mod"] for r in b})))
    return paths


def main():
    base = sys.argv[1]
    review = os.path.join(base, "review")
    nid, tid = 0, 0
    ne, ne_ign, ne_man = classify(os.path.join(base, "nonenglish.tsv"), {"def", "key"})
    ty, ty_ign, ty_man = classify(os.path.join(base, "typos.tsv"), {"def", "key"})
    for r in ne:
        nid += 1
        r["id"] = f"N{nid:04d}"
    for r in ty:
        tid += 1
        r["id"] = f"T{tid:04d}"

    # The manual rows get their own ids and shards: they are fixed by an XML patch, not a translation file,
    # because the field is not one the game marks translatable. Numbered after the others so re-running this
    # script never moves an id a reviewer has already answered.
    patch_ne, patch_ty = [], []
    for i, r in enumerate(ne_man + ty_man, 1):
        r["id"] = f"M{i:04d}"
        (patch_ne if scan.NON_LATIN.search(r["text"]) else patch_ty).append(r)

    ncols = ["id", "mod", "kind", "target", "tag", "text"]
    tcols = ["id", "mod", "kind", "target", "tag", "word", "guess", "freq", "text"]
    npaths = shard(review, "nonenglish", ne, ncols, SHARD_NONENGLISH, True)
    tpaths = shard(review, "typos", ty, tcols, SHARD_TYPOS, False)
    pcols = ["id", "mod", "kind", "target", "tag", "file", "text"]
    npaths += shard(review, "patch-nonenglish", patch_ne, pcols, SHARD_NONENGLISH, False)
    tpaths += shard(review, "patch-typos", patch_ty, pcols + ["word", "guess", "freq"], SHARD_TYPOS, False)
    write(os.path.join(base, "ignored.tsv"), ne_ign + ty_ign,
          ["reason", "mod", "kind", "target", "tag", "file", "text"])
    write(os.path.join(base, "manual.tsv"), ne_man + ty_man,
          ["reason", "mod", "kind", "target", "tag", "file", "text"])

    lines = [f"non-English to translate {len(ne)} in {len({r['mod'] for r in ne})} mods",
             f"typo candidates to judge  {len(ty)} in {len({r['mod'] for r in ty})} mods",
             f"ignored {len(ne_ign) + len(ty_ign)}  manual {len(ne_man) + len(ty_man)}", ""]
    for p, n, mods in npaths + tpaths:
        lines.append(f"{os.path.basename(p)}  {n:4d} rows  {', '.join(mods[:4])}"
                     + (" ..." if len(mods) > 4 else ""))
    lines.append("")
    for tag, c in Counter(r["reason"] for r in ne_ign + ty_ign + ne_man + ty_man).most_common():
        lines.append(f"  {c:4d}  {tag}")
    open(os.path.join(base, "prep.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
