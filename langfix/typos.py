r"""Rank spelling candidates in the scanned player-facing text, using the corpus itself as the dictionary.

usage: py -I typos.py SCANDIR

Reads SCANDIR\strings.tsv (written by scan.py) and writes SCANDIR\typos.tsv.

No word list ships with this machine, so frequency over all 549 active mods plus the vanilla game is the
dictionary: a word every mod spells the same way thousands of times is correct, and a word that occurs once or
twice in 2.7 million strings while a common word sits one edit away is a candidate. Inflections of the same
stem (plural, possessive, -ed, -ing, -er) are never reported against each other, since both forms are real.
Name lists (Languages/<lang>/Strings) are excluded: they are invented names by design.
"""
import os
import re
import sys
from collections import Counter

WORD = re.compile(r"[A-Za-z][A-Za-z']{2,}")
PLACEHOLDER = re.compile(r"\{[^}]*\}|\[[^\]]*\]|<[^>]*>|\\n")
ALPHA = "abcdefghijklmnopqrstuvwxyz"
UI_TAGS = None  # set from scan.py

KNOWN_MIN = 10      # a word this common in the corpus is treated as correctly spelled
TARGET_MIN = 60     # only a clearly common word is offered as the correction
CAND_MAX = 2        # a real typo is rare; repeated odd words are deliberate vocabulary
MIN_LEN = 5
SUFFIX = ("s", "es", "d", "ed", "ing", "er", "ers", "y", "ly", "'s", "n", "en")


def inflection(a, b):
    """True when one word is the other plus a common ending, so neither is a misspelling of the other."""
    lo, hi = sorted((a, b), key=len)
    if hi.startswith(lo):
        return hi[len(lo):] in SUFFIX
    if len(hi) > 1 and hi[:-1].startswith(lo[:-1]) and lo[:-1] == hi[:-2]:
        return True  # carry/carries style doubling
    return False


def edit1(word, freq, target_min):
    best = None
    seen = set()
    for i in range(len(word) + 1):
        for c in ALPHA:
            seen.add(word[:i] + c + word[i:])
    for i in range(len(word)):
        seen.add(word[:i] + word[i + 1:])
        for c in ALPHA:
            seen.add(word[:i] + c + word[i + 1:])
    for i in range(len(word) - 1):
        seen.add(word[:i] + word[i + 1] + word[i] + word[i + 2:])
    seen.discard(word)
    for cand in seen:
        n = freq.get(cand, 0)
        if n >= target_min and not inflection(word, cand) and (best is None or n > best[1]):
            best = (cand, n)
    return best


def rows(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        head = fh.readline().rstrip("\n").split("\t")
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == len(head):
                yield dict(zip(head, parts))


def main():
    out = sys.argv[1]
    src = os.path.join(out, "strings.tsv")
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import scan
    ui = scan.UI_TAGS

    freq = Counter()
    kept = 0
    for r in rows(src):
        if r["kind"] == "text" or (r["tag"] not in ui and r["kind"] != "key"):
            continue
        kept += 1
        for w in WORD.findall(PLACEHOLDER.sub(" ", r["text"])):
            freq[w.lower()] += 1

    hits = []
    checked = {}
    for r in rows(src):
        if r["kind"] == "text" or (r["tag"] not in ui and r["kind"] != "key"):
            continue
        clean = PLACEHOLDER.sub(" ", r["text"])
        d = re.search(r"\b([A-Za-z]{3,})\s+\1\b", clean, re.I)
        if d:
            hits.append(dict(r, word=d.group(0), guess=d.group(1), freq="", reason="doubled word"))
            continue
        for w in WORD.findall(clean):
            lw = w.lower().strip("'")
            if len(lw) < MIN_LEN or freq[lw] > CAND_MAX:
                continue
            if lw not in checked:
                checked[lw] = edit1(lw, freq, TARGET_MIN)
            hit = checked[lw]
            if hit:
                hits.append(dict(r, word=w, guess=hit[0], freq=f"{freq[lw]}/{hit[1]}",
                                 reason="one edit from a common word"))
                break

    scan.write_tsv(os.path.join(out, "typos.tsv"), hits, ["word", "guess", "freq", "reason"])
    by_mod = Counter(h["mod"] for h in hits)
    print(f"checked strings {kept} vocabulary {len(freq)} "
          f"words seen >= {KNOWN_MIN}x {sum(1 for v in freq.values() if v >= KNOWN_MIN)}")
    print(f"candidates {len(hits)} in {len(by_mod)} mods")
    for m, c in by_mod.most_common(40):
        print(f"  {c:5d}  {m}")


if __name__ == "__main__":
    main()
