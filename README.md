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

- `launchtest.ps1`: starts RimWorld.exe, polls Player.log for the main-menu marker (an INFO line with `[GUIPerf]` and the game version, logged by WOWGAG's Performance Patch; without that mod the fallback is 600 s of log silence), kills the game, copies the logs to `runs/<stamp>/`, writes `summary.txt` and `result.json`, prints `VERDICT: PASS` or `FAIL`. Targets: ERROR lines under 200, no `HarmonyInfix`, `DivideByZero` or `ImageOpt` hits. Refuses while RimSort runs. Exit 0 pass, 1 fail, 2 harness error.
- `rimstack-sort.ps1 [-Apply]`: runs RimStack's `--check` on a copy of the live config and diffs the result. Note: `--check` does not save the sort; it saves its own test move. Use the `rimstack-sortcli` crate (see below) for a real headless sort; this script stays as the example of driving `rimstack.exe` with `RIMSTACK_CONFIG`.

## ildump/ - .NET metadata IL dumper

`dotnet run -- <dll> <MethodName> [MethodName...]` prints the declaring type and decoded IL of every method with that name, resolving user strings, MemberRef/MethodDef/Field tokens. Uses `System.Reflection.Metadata` only; never loads the target assembly. Built to read `RTClient.dll`'s `CheckStructure` (which is `ModMainPath.EndsWith("3005289691")`).

## pack/

`Install LocalPack.bat`: installer that ships inside a built pack folder (next to `Mods\` and `ModsConfig.xml`). Backs up the live ModsConfig.xml, moves older `NN-pack__*` folders to `<RimWorld>\LocalPack\Backups\<stamp>`, copies the pack's mods into `<RimWorld>\Mods`, installs the pack's ModsConfig.xml.

## rimstack-sortcli (separate)

Headless port of RimStack's Sort, built as `crates/sortcli` inside the RimStack source clone at `F:\Rimworld Mod\RimStack\src` (GPL-3.0, so it stays with that source). Copied here under `sortcli/` once finished. Build: `cargo build -p rimstack-sortcli --release` with the Rust toolchain in `%USERPROFILE%\.cargo\bin`.

## Third-party tools used

- RimStack 1.0.1 (TheForgotten69/RimStack, GPL-3.0): extracted at `F:\Rimworld Mod\RimStack\app`, source clone at `F:\Rimworld Mod\RimStack\src`. Unsigned binary from GitHub Releases.
- RimSort at `F:\Rimsort`: rewrites ModsConfig.xml whenever it is open; close it before scripted edits.

### sortcli/ contents

Copy of `crates/sortcli` (package `rimstack-sortcli`, GPL-3.0 like RimStack). It only builds inside the RimStack workspace: copy it back to `F:\Rimworld Mod\RimStack\src\crates\sortcli`, add `"crates/sortcli"` to the root `Cargo.toml` members, then `cargo build -p rimstack-sortcli --release`. Usage: `rimstack-sortcli --game DIR --workshop DIR --config DIR [--out FILE] [--json FILE]`; prints `active=N moved=M rules=R` then one line per move with its reason. `RIMSTACK_SORTCLI_SOURCES=1` adds stderr diagnostics (which copy of each mod was used, game code path, category counts). Verified 2026-10-17 on 803 mods: same set in and out, idempotent, uses local `NN-pack__` copies over workshop duplicates, applies `%LOCALAPPDATA%\RimStack\communityRules.json` when RimStack has downloaded it.

`rimstack-sortcli` also has `--problems [--problems-json FILE]` (RimStack's own checks for the active list: incompatible pairs, missing deps, duplicate packageIds, rule violations with source kind, texture overlaps), `--no-sort`, `--patches [FILE]` / `--patches-json FILE` (every Harmony target RimStack's code scan records per active mod, kind = harmony:prefix/postfix/transpiler/finalizer or call), and `--overlaps PACKAGEID` (targets that mod shares with other active mods). `pack/migrate-settings.py` moves `Config\Mod_<workshopid>_*.xml` settings to the pack folder names (dry run by default, `--apply` copies after zipping a backup).
