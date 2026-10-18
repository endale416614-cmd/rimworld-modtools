"""Update the Homebrew README for the RimWorld 1.7 build (no Pack Core, Prepatcher or Image Opt).
Each replacement must match exactly once.   py -I hb17readme.py IN.txt OUT.txt INSTALL_GB EXTRACT_GB"""
import sys

src, dst, install_gb, extract_gb = sys.argv[1:5]
t = open(src, encoding='utf-8-sig').read()
R = [
    (" - Use RimSort's texture optimization. The pack already optimizes textures (Image Opt + texture bundles).\n",
     " - Use RimSort's texture optimization. It is not tested with RimWorld 1.7.\n"),
    (" - Needs RimWorld 1.6 with all 5 DLCs, kept up to date by Steam (same version as the host).",
     " - Needs RimWorld 1.7 with all 5 DLCs, kept up to date by Steam (same version as the host)."),
    (' - The main menu shows "Homebrew Modpack v1.7" in the bottom-left corner.\n', ''),
    ("  - RimWorld 1.6 (Steam or GOG) with ALL five DLCs: Royalty, Ideology, Biotech, Anomaly, Odyssey.",
     "  - RimWorld 1.7 (Steam or GOG) with ALL five DLCs: Royalty, Ideology, Biotech, Anomaly, Odyssey."),
    ("About 3.0 GB of free space", f"About {install_gb} GB of free space"),
    ("(plus about 2.3 GB wherever you extract the download)", f"(plus about {extract_gb} GB wherever you extract the download)"),
    ('  changing mods in the game\'s Mods menu - makes the server refuse you. "Sort" would also move\n'
     '  "Homebrew - Pack Core" off the top, and the pack needs it there.\n',
     "  changing mods in the game's Mods menu - makes the server refuse you.\n"),
    ('    never press "Sort"; "Homebrew - Pack Core" must stay above Prepatcher.\n', '    never press "Sort".\n'),
]
for old, new in R:
    n = t.count(old)
    if n != 1: sys.exit(f'expected 1 match, found {n}: {old[:70]!r}')
    t = t.replace(old, new)
t = t.replace('\nGood to know\n', '\nGood to know\n'
              ' - RimWorld 1.7 build: every mod is a normal folder now (the 1.6 packs needed Pack Core and\n'
              '   Prepatcher, which do not run on 1.7). Not included any more: Prepatcher, Image Opt,\n'
              '   Visible Error Logs, Graphics Settings, No Modlist on Loading, Surrogate Mechanoid.\n', 1)
open(dst, 'w', encoding='utf-8-sig', newline='\r\n').write(t)
print('ok')
