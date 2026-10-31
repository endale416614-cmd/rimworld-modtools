# rimworld-modtools

Scripts built on 2026-10-17 while moving an 800-mod RimWorld list to 1.7 (Waylith engine). Local workstation tooling; paths default to this machine (RimWorld under `F:\Steam\steamapps\common\RimWorld`, workshop under `F:\Steam\steamapps\workshop\content\294100`, config under `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config`). Not meant for a public remote as is.

Python scripts run with `py -I script.py ...`. PowerShell scripts are Windows PowerShell 5.1.

## audit/ - mod list audit pipeline

Run in this order; each step feeds the next.

| Script | Input | Output | What it does |
|---|---|---|---|
| `scan.py active.txt mods.json` | one packageId per line (from ModsConfig.xml) | `mods.json` | Parses every About.xml under the workshop, local Mods and Data folders: name, versions, deps, incompatibleWith, loadAfter/Before, assemblies, duplicate installs. |
| `analyze.py mods.json` | | text report | Declared incompatibles, missing dependencies, load-order violations, duplicate folders for the active list. |
| `dllscan.py mods.json` | | text | Greps active mods' DLLs for type names (edit `pats`). |
| `packs.py mods.json outdir` | | `outdir/packs.json` | Assigns each active mod to one of 17 packs (regex rules plus the `OVER` override table). |
| `report.py mods.json packs.json outdir` | | `REPORT.md`, `LoadOrder.proposed.txt`, `ModsConfig.proposed.xml` | Hard-remove list (max supported version < 1.6), config faults, overlaps, pack-ordered load order with a dependency-respecting topological sort (lifts a mod into the latest pack of anything it must load after). |
| `logdetail.py Player.log` | | text | Groups a 1.7 Player.log by message family: texture failures by mod folder, exception stacks, warnings, other errors. |
| `buildpack.py mods.json LoadOrder.proposed.txt packdir` | | `packdir/Mods/NN-pack__packageId/...`, `release.json`, `LoadOrder.txt`, `ModsConfig.xml` | Copies each active mod into a Homebrew-style local pack, slimmed (Source and unused version folders dropped, LoadFolders.xml honoured). Edit `REMOVE` for mods to leave out. |
| `removemods.py packdir ModsConfig.xml` | | updates pack + config | Removes a mod list (`REMOVE` table) from the pack folders, release.json, LoadOrder.txt and a config file. |

Known limits: `packs.py` regexes and `OVER` are tuned to one specific list; `report.py` treats a mod whose About.xml lists a dependency that in turn says loadAfter the parent as a cycle and ignores the parent side.

## harness/ - launch and sort automation

- `launchtest.ps1`: starts RimWorld.exe, polls Player.log for the main-menu marker (an INFO line with `[GUIPerf]` and the game version, logged by WOWGAG's Performance Patch; without that mod the fallback is 600 s of log silence), kills the game, copies the logs to `runs/<stamp>/`, writes `summary.txt` and `result.json`, prints `VERDICT: PASS` or `FAIL`. Targets: ERROR lines under 200, no `HarmonyInfix`, `DivideByZero` or `ImageOpt` hits. Refuses while RimSort runs. Exit 0 pass, 1 fail, 2 harness error. The marker and the `[Verse] RimWorld` line match any `RimWorld 1.7.<n> rev<n>` build (they were hard-coded to 1.7.4981 rev136 until 2026-10-23). `-SettleSec N` (default 20) is the wait after the marker before the kill; use 60 to also catch warnings logged after the main menu (Show Me Your Hands, YART, Melee Animation log 21-26 s later).
- `rimstack-sort.ps1 [-Apply]`: runs RimStack's `--check` on a copy of the live config and diffs the result. Note: `--check` does not save the sort; it saves its own test move. Use the `rimstack-sortcli` crate (see below) for a real headless sort; this script stays as the example of driving `rimstack.exe` with `RIMSTACK_CONFIG`.

## probe/ - runtime scan of the active list

`probe\Probe.cs` compiles into LP17 Hotfix only with `dotnet build -c Release -p:Probe=true -o bin/ProbeBuild`. `harness\proberun.ps1 -Mode static|soak [-Save NAME] -OutLog FILE [-OutTable FILE]` swaps that DLL in, turns dev mode on, launches, waits for `[PROBE] done`, kills the game, then restores the DLL and Prefs.xml. `static` compiles every mod method (generics included) and checks Harmony targets and comp property classes; `soak` loads a save with autosave off and exercises UI tabs, defs, pawn kinds, spawns, incidents and quests. It never saves. `LP17_PROBE_SKIP` is a comma list of defNames not to spawn. Many soak errors are artefacts of spawning things without their setup, so read stacks before fixing.

## ildump/ - .NET metadata IL dumper

`dotnet run -- <dll> <MethodName> [MethodName...]` prints the declaring type and decoded IL of every method with that name, resolving user strings, MemberRef/MethodDef/Field tokens. Uses `System.Reflection.Metadata` only; never loads the target assembly. Built to read `RTClient.dll`'s `CheckStructure` (which is `ModMainPath.EndsWith("3005289691")`).

## pack/

`tag17.py MODSDIR [MODSDIR...] [--apply]`: marks local forks as 1.7 by adding `<li>1.7</li>` to About.xml `supportedVersions`. Dry run by default; `--apply` keeps `<file>.pre17` backups (never overwritten) and skips any edit that would not parse. Metadata only: it does not recompile DLLs. Run with no arguments for its self-check. It does not touch LoadFolders.xml: copying the newest entry as `v1.7` was tried on 2026-10-18 and stalled the launch about 5 min before Harmony loaded, and it changed what loaded (new ShowMeYourHands and YART warnings); without a `v1.7` entry the game falls back to the newest older entry, which is the tested path. Applied 2026-10-18 to both `F:\Rimworld Mod\LocalPack-v1.7\Mods` and `<RimWorld>\Mods` (461 About.xml each); harness run 20261018_121325 PASS, 0 errors.

`Install LocalPack.bat`: installer that ships inside a built pack folder (next to `Mods\` and `ModsConfig.xml`). Backs up the live ModsConfig.xml, moves older `NN-pack__*` folders to `<RimWorld>\LocalPack\Backups\<stamp>`, copies the pack's mods into `<RimWorld>\Mods`, installs the pack's ModsConfig.xml.

### Homebrew Modpack for 1.7 (`hb17*.py`)

The Homebrew pack (HBP_* folders with `Members/`) needs Pack Core, which needs Prepatcher, which is Mono-only and cannot run on Waylith/.NET 10. These scripts rebuild the pack's data as plain mod folders; see the docstring of each. Order:

1. `hb17build.py flatten SRC OUT bundlex.exe`: SRC = extracted `Modpack-v1.7.data.zip`. Workshop members and workshop `keep` folders come from the current workshop copy (slimmed, `.dds` twins left out); local members come from the pack, with a LoadFolders.xml rebuilt from the pack's, and their textures decoded from the pack's BC7 bundle. `DROP` lists the mods left out and why. Writes `OUT/BUILD-LOG.txt`.
2. `refcheck` on `OUT/Mods`, then `patch17` on whatever it flags, then `tag17.py OUT/Mods --apply`. Move every `*.pre17` out of OUT before zipping.
3. `hb17build.py release SRC OUT "1.7.4981 rev136"`: ModsConfig.xml (Pack Core's LoadOrder.txt minus DROP), settings files of kept folders, `release.json` (files/bytes/treeHash/dlls computed exactly like the installer's `Util.Measure`/`TreeHash`, checked against the original release).
4. `hb17installer.py IN.bat OUT.bat GAMEBUILD STAMP`: installer for 1.7 (RimWorld.exe, game version, retired ids); the base64 payload after `##PAYLOAD` is kept byte for byte.
5. `hb17readme.py IN OUT INSTALL_GB EXTRACT_GB` and `hb17zip.py BUILD OUT.zip` (verifies config and DLL hashes in the zip).

### LocalPack release (`lp17*.py`)

The LocalPack ships in the same format as the Homebrew 1.7 build (installer .bat + data zip + README), with the tested live load order and the mods in category-prefixed folders (`NN-category__packageid`).

1. `lp17release.py PACK OUT CONFIGDIR "GAMEBUILD"`: PACK = `F:\Rimworld Mod\LocalPack-v1.7` (a git repo), CONFIGDIR = the Config folder whose ModsConfig.xml was launch-tested. Asserts that the pack folders and the load order match one-to-one and that every folder name matches its About.xml packageId. Hard-links Mods into OUT without `*.orig`/`*.pre17`, copies ModsConfig.xml and the settings files of shipped folders (skips FasterGameLoading's cache and any file holding a local path), writes `release.json` (installer schema plus `packs` categories and each entry's `category`), `LoadOrder.txt`, `CATEGORIES.txt`.
2. `hb17zip.py OUT LocalPack-v1.7.data.zip`.
3. `lp17installer.py "Install Homebrew Modpack.bat" "Install LocalPack.bat" STAMP RETIRED_IDS`: rebrands the Homebrew 1.7 installer (product name, tool folder `<RimWorld>\LocalPack`, data zip name, retired ids); every replacement must match exactly, and no Homebrew text may remain.

Headless installer test: `HBM_ACTION=install HBM_YES=1 HBM_TEST=1 HBM_GAME_DIR=<stub> HBM_CONFIG_DIR=<dir> HBM_DESKTOP_DIR=<dir>`; a stub game folder needs only `Version.txt`, a `RimWorld.exe` file and `Data\<DLC>\About\About.xml`.

## refcheck/ - static 1.7 compatibility check

`refcheck --game <RimWorld> --out FILE.json [--active PACKAGEIDS.txt] [--lib DIR]... MODROOT...` resolves every TypeRef and MemberRef (full signature, base-type chain, type forwarders) of every mod DLL the game would load on 1.7 (LoadFolders-aware, later entries win same-name ties) against the game's assemblies (Unity module refs map to `UnityStubs.dll`) and the other mods' DLLs. Each unresolved reference would throw TypeLoad/MissingMethod/MissingField when its caller is compiled. `--lib` adds assemblies that load before every mod (e.g. a replacement Harmony). Known false positives: the ILRepacked `0Harmony.dll` (Harmony 3) and MessagePack's `AssemblyBuilder.Save`; `MissingAssembly` hits are optional compat code for absent mods. It cannot see Harmony patch targets named by string, reflection by name, or IL the runtime rejects (use the harness for those).

## patch17/ - IL patches for 1.6 DLLs on 1.7

`patch17 IN.dll OUT.dll [--ref DIR]...` (Mono.Cecil 0.11.6) rewrites only the instructions that refcheck flagged, one rule each: `List.EnsureCapacity` void to int + pop, `Emit.Label.label` to `box` + `GetHashCode()`, `TerrainDef.holdSnow` to `holdSnowOrSand`, `Widgets.ThingIcon` 5 to 7 args (defaults), `Input.ResetInputAxes` to nop, Harmony 2's `DefaultInterpolatedStringHandler` polyfill to `System.Runtime`, Replace Stuff's removed `NewThingDefOf` to an injected `[DefOf]` class. Also (whole-method rule) Graphics Settings+'s `LoadTexture_Patch.Prefix`: falls back to vanilla when the file is not on disk or its custom load fails (1.7 DLC textures are in `Assets.zip` content packs). Mod-specific bug-fix rules (2026-10-18, RW-MOD-013; details in the pack's FIXES-dll.md): More Vanilla Mechanoid field lookup walks base types (injected `FindField`), UsefulMarks looks for `ColoredMoodBar17`, Isekai Leveling uses `AccessTools.DeclaredMethod` for MindWipe.Cast, Eccentric Projectiles hooks every `DrawEquipmentAiming` call (Dual Wield adds one). `--scos FILE` adds `[Verse.StaticConstructorOnStartup]` to the types listed in FILE (`scos17.txt`: 37 types whose static constructors were checked to be safe after defs load). `UnityEngine.*` assembly refs resolve to `UnityStubs.dll`, as in the game. Applied 2026-10-18 to Adaptive Storage Framework, SpeakUp, PowerTab, Choose Your Outfit, Fortification Medieval and Replace Stuff Compatibility in both LocalPack copies (`.pre17` backups next to each DLL); harness run 20261018_131258 PASS, 0 errors. Added 2026-10-23 (RW-MOD-014): `UnityEngine.Object.set_hideFlags` (a set-only no-op stub on Waylith that logs "Stub!") becomes `pop; pop`; Deep Swimming's `BundleFor` default branch loads `deepswimming_linux` (the bundle with Vulkan programs) and `ApiFitsBundle` returns true. Some DLLs need `--ref` to the folder of a DLL they reference (IsekaiDungeons: the Isekai Leveling `Assemblies` folder). Do not patch Lunar Framework component DLLs (`Lunar\Components\*.dll`): each has a `.lfc` hash file and Lunar refuses to load a changed DLL.

## hotfix/ - LocalPack Hotfixes mod

Harmony runtime fixes as one mod, `local.lp17.hotfix` (folder `16-compat__local.lp17.hotfix`, last in the active list). Build: `dotnet build -c Release` in `hotfix\` (net8.0 output compiled against the game folder's own System.Private.CoreLib/System.Runtime 10.0, Assembly-CSharp, UnityStubs and the Harmony mod's 0Harmony; works with the .NET 8 SDK), then copy `bin\Release\LP17Hotfix.dll` to `1.7\Assemblies\` of both mod copies (`<RimWorld>\Mods` and the LocalPack). Mod types are reached by name only, so a missing mod or changed signature skips that fix. Each fix logs `[LP17 Hotfix] <id>: applied` or `skipped (<reason>)`: `sound-before-ui`, `pwu-info-notices`, `meleeanim-info-notices`, `scos-notice-videoplayerutil` (from the Mod constructor), `trains-spawn-hook`, `vge-srr-unpatch`, `smyh-known-patch`, `yart-layer-separation`, `translation-dump` (startup pass; dev mode only, writes `Config\TranslationErrors-<language>.tsv` with the game's own translation load errors). Root causes: the pack's FIXES-warnings.md, RW-MOD-014.

## bundlex/ - Unity bundle texture export

`bundlex BUNDLE OUTDIR [NAMES.txt]` writes each Texture2D of an AssetBundle as PNG under its container path. It uses the game's own `AssetsTools.NET`, `AssetsTools.NET.Texture` and `AssetRipper.TextureDecoder` (HintPaths into the RimWorld folder, so no NuGet packages) and a built-in PNG writer.

`.config/dotnet-tools.json` pins ilspycmd 9.1.0.7988 (`dotnet ilspycmd`); 11.x needs the .NET 10 SDK.

## rimstack-sortcli (separate)

Headless port of RimStack's Sort, built as `crates/sortcli` inside the RimStack source clone at `F:\Rimworld Mod\RimStack\src` (GPL-3.0, so it stays with that source). Copied here under `sortcli/` once finished. Build: `cargo build -p rimstack-sortcli --release` with the Rust toolchain in `%USERPROFILE%\.cargo\bin`.

## Third-party tools used

- RimStack 1.0.1 (TheForgotten69/RimStack, GPL-3.0): extracted at `F:\Rimworld Mod\RimStack\app`, source clone at `F:\Rimworld Mod\RimStack\src`. Unsigned binary from GitHub Releases.
- RimSort at `F:\Rimsort`: rewrites ModsConfig.xml whenever it is open; close it before scripted edits.

### sortcli/ contents

Copy of `crates/sortcli` (package `rimstack-sortcli`, GPL-3.0 like RimStack). It only builds inside the RimStack workspace: copy it back to `F:\Rimworld Mod\RimStack\src\crates\sortcli`, add `"crates/sortcli"` to the root `Cargo.toml` members, then `cargo build -p rimstack-sortcli --release`. Usage: `rimstack-sortcli --game DIR --workshop DIR --config DIR [--out FILE] [--json FILE]`; prints `active=N moved=M rules=R` then one line per move with its reason. `RIMSTACK_SORTCLI_SOURCES=1` adds stderr diagnostics (which copy of each mod was used, game code path, category counts). Verified 2026-10-17 on 803 mods: same set in and out, idempotent, uses local `NN-pack__` copies over workshop duplicates, applies `%LOCALAPPDATA%\RimStack\communityRules.json` when RimStack has downloaded it.

`rimstack-sortcli` also has `--problems [--problems-json FILE]` (RimStack's own checks for the active list: incompatible pairs, missing deps, duplicate packageIds, rule violations with source kind, texture overlaps), `--no-sort`, `--patches [FILE]` / `--patches-json FILE` (every Harmony target RimStack's code scan records per active mod, kind = harmony:prefix/postfix/transpiler/finalizer or call), and `--overlaps PACKAGEID` (targets that mod shares with other active mods). `pack/migrate-settings.py` moves `Config\Mod_<workshopid>_*.xml` settings to the pack folder names (dry run by default, `--apply` copies after zipping a backup).
