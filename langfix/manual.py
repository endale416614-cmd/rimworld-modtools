r"""Write the handful of verdicts the review agents left to a human, as a verdict file build.py reads.

usage: py -I manual.py SCANDIR

Each entry below names the row id and how the replacement is derived from the original text, so the fix stays
tied to the string actually in the mod rather than a copy typed out here.
"""
import os
import re
import sys

# id -> edits applied in order to the original text, each (pattern, replacement, pattern is a regex).
EDITS = {
    # Cyrillic C in an English label; renders as a Latin C but is a different character.
    "N0013": [("Сould", "Could", False)],
    # Katakana middle dot between two English words.
    "N0014": [("・", " ", False)],
    "N0027": [("・", " ", False)],
    # A line break token that lost its n, so the text shows a literal backslash.
    "T0339": [(r"\\(?=Pejoratively)", r"\\n", True)],
    # The scenario ships a Japanese block and an English one, both shown. Keep the English block the mod
    # already wrote, with its em dashes turned into plain ASCII.
    "N0023": [(r"(?s)^.*\[EN\]\\n", "", True), ("—", "-", False)],
}


def rows(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        head = fh.readline().rstrip("\n").split("\t")
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= len(head):
                yield dict(zip(head, parts[:len(head) - 1] + ["\t".join(parts[len(head) - 1:])]))


def main():
    base = sys.argv[1]
    originals = {}
    for f in sorted(os.listdir(os.path.join(base, "review"))):
        for r in rows(os.path.join(base, "review", f)):
            originals[r["id"]] = r["text"]

    out, missed = [], []
    for rid, edits in EDITS.items():
        old = originals.get(rid)
        if old is None:
            missed.append(f"{rid} no such row")
            continue
        new = old
        for pat, rep, is_regex in edits:
            new = re.sub(pat, rep, new) if is_regex else new.replace(pat, rep)
        if new == old:
            missed.append(f"{rid} no edit matched {old[:60]!r}")
            continue
        out.append((rid, new))

    path = os.path.join(base, "verdicts", "manual-01.out.tsv")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as fh:
        fh.write("id\tnew\n")
        for rid, new in sorted(out):
            fh.write(f"{rid}\t{new}\n")
    print(f"wrote {len(out)} rows to {path}")
    for m in missed:
        print("SKIPPED", m)


if __name__ == "__main__":
    main()
