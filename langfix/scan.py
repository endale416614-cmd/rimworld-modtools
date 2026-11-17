r"""Find non-English and misspelled player-facing text in the active mod list.

usage: py -I scan.py OUTDIR [ModsConfig.xml]

Resolves every active mod to the folders the game loads (reusing audit/listcheck.py), then reads only the
content the game shows in English: Defs, Patches, Languages/<active language>/{Keyed,DefInjected,Strings}.
Other language folders are skipped because the game never loads them.

Writes to OUTDIR:
  nonenglish.tsv   every string containing a non-Latin script (CJK, Hangul, Kana, Cyrillic, Thai, Arabic,
                   Hebrew, Devanagari), with the mod, file, kind and target path a fix needs
  typos.tsv        rare words one edit away from a word the vanilla game or several mods use, plus
                   doubled words, in the same format
  strings.tsv      every player-facing string that carried a target path (input for review)
  summary.txt      counts per mod

Target path forms (what the override table consumes):
  def   <DefType>/<defName>/<field path>
  key   <keyed translation key>
  text  <file relative to the mod folder>:<line>
Reads only.
"""
import os
import re
import sys
import unicodedata
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "audit"))
import listcheck as lc

CONFIG = os.path.expandvars(
    r"%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml")
PREFS = os.path.expandvars(
    r"%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\Prefs.xml")

# Scripts that are never correct in English player-facing text.
NON_LATIN = re.compile(
    "[Ѐ-ӿԀ-ԯ"       # Cyrillic
    "֐-׿؀-ۿ"        # Hebrew, Arabic
    "ऀ-ॿ฀-๿"        # Devanagari, Thai
    "ᄀ-ᇿ㄰-㆏가-힯"   # Hangul
    "぀-ヿㇰ-ㇿ"        # Kana
    "㐀-䶿一-鿿豈-﫿"   # CJK
    "！-｠]")                   # full-width forms

# Tags whose text the player reads. Used for spell checking; non-Latin is reported from any tag.
UI_TAGS = {
    "label", "labelshort", "labelplural", "labelmale", "labelfemale", "labelnoun", "labelcap",
    "description", "descriptionshort", "descriptionfuture", "desc", "basedesc", "tooltip",
    "jobstring", "reportstring", "gerund", "gerundlabel", "verb", "customlabel", "pawnlabel",
    "title", "titleshort", "titlefemale", "titleshortfemale", "grouplabel", "headerlabel",
    "letterlabel", "lettertext", "beginletter", "beginletterlabel", "text", "message",
    "successmessage", "failmessage", "endmessage", "stuffadjective", "ingestcommandstring",
    "ingestreportstring", "onmapinstruction", "inspectstring", "summary", "adjective", "shortdesc",
    "li",  # list entries under a UI tag; filtered by the parent chain below
}
WORD = re.compile(r"[A-Za-z][A-Za-z']{2,}")
PLACEHOLDER = re.compile(r"\{[^}]*\}|\[[^\]]*\]|\([^)]*\)")
DOUBLED = re.compile(r"\b([A-Za-z]{3,})\s+\1\b", re.I)
ALPHA = "abcdefghijklmnopqrstuvwxyz"


def active_language():
    try:
        root = ET.parse(PREFS).getroot()
        v = root.findtext("langFolderName")
        return v.strip() if v and v.strip() else "English"
    except Exception:
        return "English"


def parse(path):
    try:
        return ET.parse(path).getroot()
    except Exception:
        return None


def ui_path(chain):
    """Field path under a def node, e.g. ['comps', 'li', 'label'] -> comps.0.label handled by caller."""
    return ".".join(chain)


def walk(node, chain, out):
    """Collect (field path, tag, text) for every element with text."""
    counts = Counter()
    for child in node:
        if not isinstance(child.tag, str):
            continue
        counts[child.tag] += 1
        idx = counts[child.tag] - 1
        step = child.tag if child.tag != "li" else str(idx)
        sub = chain + [step]
        txt = (child.text or "").strip()
        if txt:
            out.append((".".join(sub), child.tag.lower(), txt))
        walk(child, sub, out)


def scan_defs(root, mod_id, rel, rows):
    for defnode in root:
        if not isinstance(defnode.tag, str):
            continue
        name = (defnode.findtext("defName") or defnode.get("Name") or "").strip()
        if not name:
            continue
        got = []
        walk(defnode, [], got)
        for field, tag, txt in got:
            if field == "defName":
                continue
            rows.append(dict(mod=mod_id, file=rel, kind="def",
                             target=f"{defnode.tag}/{name}/{field}", tag=tag, text=txt))


def scan_keyed(root, mod_id, rel, rows):
    for child in root:
        if not isinstance(child.tag, str):
            continue
        txt = (child.text or "").strip()
        if txt:
            rows.append(dict(mod=mod_id, file=rel, kind="key", target=child.tag,
                             tag="keyed", text=txt))


def scan_definjected(root, mod_id, rel, deftype, rows):
    for child in root:
        if not isinstance(child.tag, str):
            continue
        txt = (child.text or "").strip()
        if not txt:
            continue
        parts = child.tag.split(".", 1)
        target = f"{deftype}/{parts[0]}/{parts[1]}" if len(parts) == 2 else f"{deftype}/{child.tag}/label"
        tag = parts[1].split(".")[-1].lower() if len(parts) == 2 else "label"
        rows.append(dict(mod=mod_id, file=rel, kind="def", target=target, tag=tag, text=txt))


def scan_patch(root, mod_id, rel, rows):
    for op in root.iter():
        if not isinstance(op.tag, str):
            continue
        xpath = op.findtext("xpath")
        if not xpath:
            continue
        value = op.find("value")
        if value is None:
            continue
        got = []
        walk(value, [], got)
        for field, tag, txt in got:
            rows.append(dict(mod=mod_id, file=rel, kind="patch",
                             target=f"{xpath.strip()}|{field}", tag=tag, text=txt))


def scan_strings(path, mod_id, rel, rows):
    try:
        lines = open(path, encoding="utf-8-sig", errors="replace").read().splitlines()
    except Exception:
        return
    for n, line in enumerate(lines, 1):
        line = line.strip()
        if line and not line.startswith("#"):
            rows.append(dict(mod=mod_id, file=rel, kind="text", target=f"{rel}:{n}",
                             tag="strings", text=line))


def collect(folders, mod_id, lang, rows):
    for folder in folders:
        for dirpath, dirnames, files in os.walk(folder):
            # The game reads defs, patches and translations only from these trees directly inside a load
            # folder. A mod can keep XML anywhere else - HugsLib news under News\, a bundled copy of another
            # mod under Mods\ - and the def loader never sees it, so neither an injection nor a patch could
            # ever reach a string found there.
            rel = os.path.relpath(dirpath, folder).replace("\\", "/").lower()
            if rel != "." and rel.split("/")[0] not in ("defs", "patches", "languages"):
                dirnames[:] = []
                continue
            low = dirpath.replace("\\", "/").lower()
            if "/languages/" in low + "/":
                # keep only the active language tree
                after = low.split("/languages/", 1)[1] if "/languages/" in low else ""
                top = after.split("/", 1)[0]
                if top and top != lang.lower():
                    dirnames[:] = []
                    continue
            if "/source/" in low or "/.git" in low:
                dirnames[:] = []
                continue
            for f in files:
                path = os.path.join(dirpath, f)
                rel = os.path.relpath(path, folder)
                ext = os.path.splitext(f)[1].lower()
                if ext == ".txt" and "/strings" in low:
                    scan_strings(path, mod_id, rel, rows)
                    continue
                if ext != ".xml":
                    continue
                root = parse(path)
                if root is None:
                    continue
                if "/definjected" in low:
                    scan_definjected(root, mod_id, rel, os.path.basename(dirpath), rows)
                elif "/keyed" in low:
                    scan_keyed(root, mod_id, rel, rows)
                elif root.tag == "Patch":
                    scan_patch(root, mod_id, rel, rows)
                elif root.tag == "Defs":
                    scan_defs(root, mod_id, rel, rows)
                elif root.tag == "LanguageData":
                    scan_keyed(root, mod_id, rel, rows)


def vanilla_words(lang):
    words = Counter()
    data = os.path.join(lc.GAME, "Data")
    for pkg in sorted(os.listdir(data)):
        base = os.path.join(data, pkg)
        if not os.path.isdir(base):
            continue
        rows = []
        collect([base], pkg, lang, rows)
        for r in rows:
            for w in WORD.findall(PLACEHOLDER.sub(" ", r["text"])):
                words[w.lower()] += 1
    return words


def edit1_hit(word, known):
    for i in range(len(word) + 1):
        for c in ALPHA:
            if word[:i] + c + word[i:] in known:
                return word[:i] + c + word[i:]
    for i in range(len(word)):
        if word[:i] + word[i + 1:] in known:
            return word[:i] + word[i + 1:]
        for c in ALPHA:
            if c != word[i] and word[:i] + c + word[i + 1:] in known:
                return word[:i] + c + word[i + 1:]
    for i in range(len(word) - 1):
        swap = word[:i] + word[i + 1] + word[i] + word[i + 2:]
        if swap in known:
            return swap
    return None


def write_tsv(path, rows, extra=()):
    cols = ["mod", "kind", "target", "tag", "file", "text"] + list(extra)
    with open(path, "w", encoding="utf-8", newline="") as fh:
        fh.write("\t".join(cols) + "\n")
        for r in rows:
            fh.write("\t".join(str(r.get(c, "")).replace("\t", " ").replace("\n", "\\n")
                               for c in cols) + "\n")


def main():
    out = sys.argv[1]
    cfg = sys.argv[2] if len(sys.argv) > 2 else CONFIG
    os.makedirs(out, exist_ok=True)
    lang = active_language()
    active_raw = [(li.text or "").strip().lower()
                  for li in ET.parse(cfg).getroot().find("activeMods")]
    ws = lc.index(lc.WORKSHOP)
    local = lc.index(os.path.join(lc.GAME, "Mods"))
    data = lc.index(os.path.join(lc.GAME, "Data"))
    normset = {lc.norm(i) for i in active_raw}

    rows, missing, scanned = [], [], 0
    for raw in active_raw:
        pid = lc.norm(raw)
        if pid in data:
            continue  # vanilla and DLC are the reference, not a target
        src = ws if raw.endswith("_steam") else local
        mod = src.get(pid) or (local.get(pid) if raw.endswith("_steam") else ws.get(pid))
        if not mod:
            missing.append(raw)
            continue
        folders = lc.load_folders(mod["dir"], normset)
        before = len(rows)
        collect(folders, pid, lang, rows)
        scanned += 1
        if len(rows) == before:
            pass

    # vocabulary: vanilla words plus any word several mods use
    vanilla = vanilla_words(lang)
    per_word_mods = defaultdict(set)
    for r in rows:
        if r["tag"] in UI_TAGS or r["kind"] in ("key", "text"):
            for w in WORD.findall(PLACEHOLDER.sub(" ", r["text"])):
                per_word_mods[w.lower()].add(r["mod"])
    known = set(vanilla) | {w for w, m in per_word_mods.items() if len(m) >= 3}

    nonenglish, typos = [], []
    for r in rows:
        if NON_LATIN.search(r["text"]):
            r = dict(r, scripts=" ".join(sorted({unicodedata.name(c, "?").split()[0]
                                                 for c in r["text"] if NON_LATIN.match(c)})))
            nonenglish.append(r)
            continue
        if r["tag"] not in UI_TAGS and r["kind"] not in ("key", "text"):
            continue
        clean = PLACEHOLDER.sub(" ", r["text"])
        d = DOUBLED.search(clean)
        if d:
            typos.append(dict(r, word=d.group(0), guess=d.group(1), reason="doubled word"))
            continue
        for w in WORD.findall(clean):
            lw = w.lower()
            if lw in known or len(lw) < 4 or len(per_word_mods[lw]) > 2:
                continue
            hit = edit1_hit(lw, known)
            if hit:
                typos.append(dict(r, word=w, guess=hit, reason="one edit from a known word"))
                break

    write_tsv(os.path.join(out, "strings.tsv"), rows)
    write_tsv(os.path.join(out, "nonenglish.tsv"), nonenglish, ["scripts"])
    write_tsv(os.path.join(out, "typos.tsv"), typos, ["word", "guess", "reason"])

    by_mod = Counter(r["mod"] for r in nonenglish)
    by_mod_t = Counter(r["mod"] for r in typos)
    with open(os.path.join(out, "summary.txt"), "w", encoding="utf-8") as fh:
        fh.write(f"language {lang}\nactive {len(active_raw)} scanned {scanned} "
                 f"missing {len(missing)}\nstrings {len(rows)} "
                 f"non-English {len(nonenglish)} typo candidates {len(typos)}\n"
                 f"vanilla vocabulary {len(vanilla)} known words {len(known)}\n\n")
        fh.write("non-English per mod\n")
        for m, c in by_mod.most_common():
            fh.write(f"  {c:6d}  {m}\n")
        fh.write("\ntypo candidates per mod\n")
        for m, c in by_mod_t.most_common():
            fh.write(f"  {c:6d}  {m}\n")
        if missing:
            fh.write("\nunresolved ids\n")
            for m in missing:
                fh.write(f"  {m}\n")
    print(open(os.path.join(out, "summary.txt"), encoding="utf-8").read()[:2000])


if __name__ == "__main__":
    main()
