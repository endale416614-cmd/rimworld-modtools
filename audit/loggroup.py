"""Groups a Waylith Player.log by message family.

usage: py -I loggroup.py Player.log [LEVELS] [TOP] [MIN_LINE]

LEVELS is a comma list (default WARN,ERROR). Digits, hex ids, quoted text and paths are folded so repeats of one
message count as one family. Prints count, first line number, level, family, then one unfolded example.
"""
import re, sys
from collections import OrderedDict

HEAD = re.compile(r"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+\s+(\w+)\s+(?:\[([^\]]+)\]\s*)?(.*)$")
FOLD = [(re.compile(r"[A-Za-z]:\\[^\s'\"|]+"), "<path>"), (re.compile(r"'[^']*'|\"[^\"]*\""), "<q>"),
        (re.compile(r"0x[0-9A-Fa-f]+"), "<hex>"), (re.compile(r"\d+([.,]\d+)*"), "#")]


def main():
    path = sys.argv[1]
    levels = set((sys.argv[2] if len(sys.argv) > 2 else "WARN,ERROR").split(","))
    top = int(sys.argv[3]) if len(sys.argv) > 3 else 60
    start = int(sys.argv[4]) if len(sys.argv) > 4 else 0
    fam = OrderedDict()
    with open(path, encoding="utf-8", errors="replace") as f:
        for n, line in enumerate(f, 1):
            m = HEAD.match(line)
            if not m or m[1] not in levels or n < start:
                continue
            msg = m[3].strip()
            key = msg[:160]
            for rx, rep in FOLD:
                key = rx.sub(rep, key)
            key = (m[1], key[:110])
            if key in fam:
                fam[key][0] += 1
            else:
                fam[key] = [1, n, msg[:260]]
    total = sum(v[0] for v in fam.values())
    print(f"{total} lines in {len(fam)} families")
    for (lvl, key), (cnt, first, example) in sorted(fam.items(), key=lambda kv: -kv[1][0])[:top]:
        print(f"{cnt:6d}  L{first:<7d} {lvl:5s} {key}\n        e.g. {example}")


if __name__ == "__main__":
    main()
