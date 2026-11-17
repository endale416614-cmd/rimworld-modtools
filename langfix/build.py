r"""Validate the reviewed strings and build the LP17 Hotfix override files.

usage: py -I build.py SCANDIR [--install]

Reads SCANDIR\review\*.tsv (the originals) and SCANDIR\verdicts\*.out.tsv (id and replacement text) and writes
into the hotfix source tree:
  hotfix\1.7\Languages\English\Keyed\LP17Text.xml
  hotfix\1.7\Languages\English\DefInjected\<DefType>\LP17Text.xml
With --install the same files are copied to the installed mod folder.

Why this works: the game reads every mod's Languages\English folder in load order and stores keyed strings and
def injections with SetOrAdd, so the last mod loaded wins. This mod is last in the list, so these values
replace the mod's own text without touching a single mod file, which a Steam update would undo anyway. The
duplicate-key message the loader records is cleared by DefInjectionPackage.InjectIntoDefs before the game
counts translation errors, so this adds no warning.

Every replacement is checked first; anything that fails lands in rejected.tsv and is left out:
  unknown id, empty or unchanged text, non-ASCII, a value of TODO (the game reads it as untranslated and
  blanks the string), a changed set of {placeholders}, <markup> tags or \n line breaks, or - for a typo fix -
  a rewrite rather than a correction.

A launch is what proves an override reaches the game, and what it proves goes in fixups.tsv (columns id, action,
xpath, element, note): action "drop" leaves a row out, action "patch" moves it out of the translation files into a
patch operation with the xpath given there. Three cases need it, all found this way:
  - a def behind a LoadFolders IfModActive gate for a mod that is not in the list never loads, so an injection
    into it can never apply
  - an abstract parent def (a Name attribute and no defName) is not in the def database, so only a patch on
    [@Name="..."] reaches it, and its children inherit the result
  - a list index is not stable: patches run before inheritance resolves, and a parent's own list items are
    prepended to the child's, so match on an attribute or on the text instead of on an index
"""
import difflib
import os
import re
import sys
import xml.sax.saxutils as sx
from collections import defaultdict

HOTFIX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "hotfix")
INSTALL = r"F:\Steam\steamapps\common\RimWorld\Mods\16-compat__local.lp17.hotfix"
TOKEN = re.compile(r"\{[^}]*\}|</?[A-Za-z][^>]*>")
NON_LATIN = None  # from scan.py
SIMILAR_MIN = 0.72  # a typo fix changes a word, not the sentence


def xpath_for(target, kind):
    """The def path an operation must target, as an xpath, plus the element name being replaced.

    A def target is DefType/defName/field.sub.0.label; a list index N becomes li[N+1], since xpath counts from
    one. A patch target is the other mod's own xpath plus the field path inside its value, and its last step
    is dropped when the value repeats it (a PatchOperationReplace names the node it replaces).
    """
    if kind == "def":
        deftype, defname, field = target.split("/", 2)
        head = f'Defs/{deftype}[defName="{defname}"]'
        steps = field.split(".")
    else:
        head, _, field = target.partition("|")
        head = head.rstrip("/")
        steps = field.split(".")
        last = head.rsplit("/", 1)[-1].split("[")[0]
        if steps and steps[0] == last:
            head = head.rsplit("/", 1)[0]
    parts = [f"li[{int(s) + 1}]" if s.isdigit() else s for s in steps]
    return head + "/" + "/".join(parts), parts[-1].split("[")[0]


def rows(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        head = fh.readline().rstrip("\n").split("\t")
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= len(head):
                yield dict(zip(head, parts[:len(head) - 1] + ["\t".join(parts[len(head) - 1:])]))


def tokens(s):
    return sorted(TOKEN.findall(s))


def main():
    base = sys.argv[1]
    install = "--install" in sys.argv
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import scan

    originals, kinds = {}, {}
    for f in sorted(os.listdir(os.path.join(base, "review"))):
        for r in rows(os.path.join(base, "review", f)):
            originals[r["id"]] = r
            kinds[r["id"]] = "typo" if f.startswith("typos") else "nonenglish"

    # fixups.tsv records what a launch proved wrong: a row the game could not apply, or an operation whose xpath
    # has to be written by hand. Columns id, action (drop or xpath), xpath, element, note.
    fixups = {}
    fpath = os.path.join(base, "fixups.tsv")
    if os.path.isfile(fpath):
        for r in rows(fpath):
            rid = r.get("id", "").strip()
            if rid:
                fixups[rid] = r

    # A mod that ships its own Languages\English entry for a path has already replaced what its def XML says, so
    # the def XML string never reaches the player. Overriding that path would throw away the author's English and
    # put a machine translation in its place. Keep the override only when the mod's own English holds the very
    # string under repair, which is the case a typo fix usually is.
    own = {}
    spath = os.path.join(base, "strings.tsv")
    if os.path.isfile(spath):
        for r in rows(spath):
            f = "/" + r["file"].replace("\\", "/").lower().lstrip("/")
            if "/languages/english" in f and ("/definjected/" in f or "/keyed/" in f):
                own.setdefault((r["mod"], r["target"]), r["text"])

    accepted, rejected = {}, []
    vdir = os.path.join(base, "verdicts")
    files = sorted(os.listdir(vdir)) if os.path.isdir(vdir) else []
    for f in files:
        # manual-*.out.tsv holds this session's own hand-checked edits, which may legitimately drop a block of
        # text or rewrite a short label; they are still checked for ASCII, placeholders and TODO.
        trusted = f.startswith("manual")
        for r in rows(os.path.join(vdir, f)):
            rid, new = r.get("id", "").strip(), r.get("new", "")
            orig = originals.get(rid)

            def bad(why):
                rejected.append(dict(id=rid, file=f, why=why, new=new,
                                     old=orig["text"] if orig else "", mod=orig["mod"] if orig else "",
                                     target=orig["target"] if orig else ""))
            if not orig:
                bad("unknown id")
                continue
            if fixups.get(rid, {}).get("action") == "drop":
                bad("dropped: " + (fixups[rid].get("note") or "a launch showed it does not apply"))
                continue
            old = orig["text"]
            author = own.get((orig["mod"], orig["target"]))
            if author is not None and author != old:
                bad("the mod's own English already replaces this: " + author[:80])
                continue
            if not new.strip():
                bad("empty")
            elif new == old:
                bad("unchanged")
            elif new.strip().upper() == "TODO":
                bad("TODO blanks the string")
            elif any(ord(c) > 126 or ord(c) < 32 for c in new):
                bad("not plain ASCII")
            elif tokens(new) != tokens(old):
                bad(f"placeholders changed: {tokens(old)} -> {tokens(new)}")
            elif new.count("\\n") < old.count("\\n") and not trusted:
                # Gaining a line break is how a broken "\n" gets repaired; losing one drops a paragraph.
                bad("line breaks lost")
            elif kinds[rid] == "nonenglish" and scan.NON_LATIN.search(new):
                bad("still not English")
            elif (kinds[rid] == "typo" and not trusted and len(old) >= 25
                  and difflib.SequenceMatcher(None, old, new).ratio() < SIMILAR_MIN):
                bad("rewrite, not a correction")
            elif rid in accepted:
                bad("duplicate verdict")
            else:
                accepted[rid] = new

    keyed, injected, patched = {}, defaultdict(dict), []
    clash = []
    for rid, new in accepted.items():
        o = originals[rid]
        if rid.startswith("M") or fixups.get(rid, {}).get("action") == "patch":
            patched.append((rid, new))
            continue
        if o["kind"] == "key":
            if o["target"] in keyed and keyed[o["target"]] != new:
                clash.append(o["target"])
                continue
            keyed[o["target"]] = new
        else:
            deftype, defname, field = o["target"].split("/", 2)
            path = f"{defname}.{field}"
            if path in injected[deftype] and injected[deftype][path] != new:
                clash.append(o["target"])
                continue
            injected[deftype][path] = new

    root = os.path.join(HOTFIX, "1.7", "Languages", "English")
    written = []

    def xml_file(path, pairs, real_newlines):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\r\n") as fh:
            fh.write('<?xml version="1.0" encoding="utf-8"?>\n')
            fh.write("<!-- LocalPack Hotfixes: English text for mods that ship another language, and\n"
                     "     spelling fixes. Generated by rimworld-modtools\\langfix\\build.py; this mod\n"
                     "     loads last, so these values replace the mod's own strings. -->\n")
            fh.write("<LanguageData>\n")
            for k in sorted(pairs):
                v = pairs[k].replace("\\n", "\n") if real_newlines else pairs[k]
                fh.write(f"  <{k}>{sx.escape(v)}</{k}>\n")
            fh.write("</LanguageData>\n")
        written.append((os.path.relpath(path, HOTFIX), len(pairs)))

    if patched:
        # These fields are not translatable ones, so they are replaced in the XML instead. The operations are
        # plain PatchOperationReplace on purpose: if one stops matching because a mod changed or left the list,
        # the game logs it at load instead of silently doing nothing, and the list is kept at zero errors.
        path = os.path.join(HOTFIX, "1.7", "Patches", "LP17Text.xml")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\r\n") as fh:
            fh.write('<?xml version="1.0" encoding="utf-8"?>\n')
            fh.write("<!-- LocalPack Hotfixes: English text and spelling fixes for fields the translation\n"
                     "     system cannot inject (custom mod fields and values another mod's patch adds).\n"
                     "     Generated by rimworld-modtools\\langfix\\build.py. -->\n<Patch>\n")
            for rid, new in sorted(patched):
                o = originals[rid]
                fix = fixups.get(rid, {})
                if fix.get("xpath"):
                    xp, tag = fix["xpath"], (fix.get("element") or xpath_for(o["target"], o["kind"])[1])
                else:
                    xp, tag = xpath_for(o["target"], o["kind"])
                value = new.replace("\\n", "\n") if o["kind"] == "def" else new
                fh.write(f'  <!-- {rid} {o["mod"]} -->\n'
                         f'  <Operation Class="PatchOperationReplace">\n'
                         f"    <xpath>{sx.escape(xp)}</xpath>\n"
                         f"    <value><{tag}>{sx.escape(value)}</{tag}></value>\n"
                         f"  </Operation>\n")
            fh.write("</Patch>\n")
        written.append((os.path.relpath(path, HOTFIX), len(patched)))

    if keyed:
        xml_file(os.path.join(root, "Keyed", "LP17Text.xml"), keyed, False)
    for deftype, pairs in sorted(injected.items()):
        xml_file(os.path.join(root, "DefInjected", deftype, "LP17Text.xml"), pairs, True)

    # A def type that loses its last override leaves its file behind, and a stale file keeps injecting the
    # string it held. Only files this script writes are named LP17Text.xml, so clearing the ones it no longer
    # generates is safe, and without it a dropped row stays live in the game.
    stale = []
    for tree in [HOTFIX] + ([INSTALL] if install else []):
        keep = {os.path.normcase(os.path.join(tree, p)) for p, _ in written}
        for sub in ("Languages", "Patches"):
            top = os.path.join(tree, "1.7", sub)
            for dirpath, _, names in (os.walk(top) if os.path.isdir(top) else []):
                for name in names:
                    path = os.path.join(dirpath, name)
                    if name == "LP17Text.xml" and os.path.normcase(path) not in keep:
                        os.remove(path)
                        stale.append(os.path.relpath(path, tree))
                        if not os.listdir(dirpath):
                            os.rmdir(dirpath)

    cols = ["id", "file", "why", "mod", "target", "old", "new"]
    with open(os.path.join(base, "rejected.tsv"), "w", encoding="utf-8", newline="") as fh:
        fh.write("\t".join(cols) + "\n")
        for r in rejected:
            fh.write("\t".join(str(r.get(c, "")).replace("\t", " ") for c in cols) + "\n")
    report = [f"verdict files {len(files)}  accepted {len(accepted)}  rejected {len(rejected)}",
              f"keyed strings {len(keyed)}  def injections "
              f"{sum(len(v) for v in injected.values())} in {len(injected)} def types"]
    if clash:
        report.append(f"conflicting verdicts for the same target, skipped: {len(clash)}")
    if stale:
        report.append("removed with no overrides left: " + ", ".join(sorted(stale)))
    for p, n in written:
        report.append(f"  {n:4d}  {p}")
    if install:
        import shutil
        for p, _ in written:
            dest = os.path.join(INSTALL, p)
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            shutil.copy2(os.path.join(HOTFIX, p), dest)
        report.append(f"installed to {INSTALL}")
    else:
        report.append("source tree only; pass --install to copy into the game")
    open(os.path.join(base, "build.txt"), "w", encoding="utf-8").write("\n".join(report) + "\n")
    print("\n".join(report))


if __name__ == "__main__":
    main()
