//! Headless Sort: the same rules, grouping and sort entry point as the app's Sort button
//! (crates/app/src/workspace/load_order/{rules,sorting}.rs), printed instead of previewed.

use rayon::prelude::*;
use rimstack_core::kept::notes::Notes;
use rimstack_core::kept::settings::{Settings, Switch};
use rimstack_core::mods::list::{Category, Mod};
use rimstack_core::mods::ordering::{self, Sources};
use rimstack_core::mods::userrules::UserRules;
use rimstack_core::mods::{community, infer, library, sort_reasons};
use rimstack_core::rimworld::paths::{self, Source};
use rimstack_core::rimworld::{loadfolders, modsconfig};
use std::collections::HashSet;
use std::path::PathBuf;

#[derive(Default)]
struct Args {
    game: Option<PathBuf>,
    workshop: Option<PathBuf>,
    config: Option<PathBuf>,
    out: Option<PathBuf>,
    json: Option<PathBuf>,
}

fn args() -> Result<Args, String> {
    let mut found = Args::default();
    let mut it = std::env::args_os().skip(1);
    while let Some(flag) = it.next() {
        let flag = flag.to_string_lossy().into_owned();
        let slot = match flag.as_str() {
            "--game" => &mut found.game,
            "--workshop" => &mut found.workshop,
            "--config" => &mut found.config,
            "--out" => &mut found.out,
            "--json" => &mut found.json,
            other => return Err(format!("unknown argument {other}")),
        };
        let value = it.next().ok_or(format!("{flag} needs a value"))?;
        *slot = Some(PathBuf::from(value));
    }
    Ok(found)
}

/// `Paths::config` is the folder holding `Config/`; the `Config` folder itself is accepted too.
fn settings_root(p: PathBuf) -> PathBuf {
    if p.join("ModsConfig.xml").is_file() {
        p.parent().map(PathBuf::from).unwrap_or(p)
    } else {
        p
    }
}

/// Stderr only: where core found the game's code and why mods got their category.
fn diagnose(library: &library::Library, mods: &[Mod], paths: &paths::Paths) {
    use rimstack_core::mods::library::Installed;
    use rimstack_core::rimworld::{game::Game, id};
    // The probe order of `Game::find`: the game folder, then each subfolder's Managed folders.
    let found = paths.game.as_ref().and_then(|g| {
        let below = std::fs::read_dir(g).ok()?.flatten().map(|e| e.path()).filter(|p| p.is_dir());
        let mut below: Vec<PathBuf> = below
            .flat_map(|p| [p.join("Managed"), p.join("Contents/Resources/Data/Managed")])
            .collect();
        below.sort();
        let places = std::iter::once(g.clone()).chain(below);
        places.map(|p| p.join("Assembly-CSharp.dll")).find(|f| f.is_file())
    });
    let loaded = paths.game.as_deref().map(|g| Game::find(g).is_some());
    eprintln!(
        "game code: path={} Game::find={:?} game_version={}",
        found.as_ref().map_or("none".into(), |f| f.display().to_string()),
        loaded,
        library.game_version
    );
    let with_code = library.installed.iter().filter(|m| m.code.facts.types > 0).count();
    let with_dlls = library.installed.iter().filter(|m| m.kinds.assemblies && m.source != Source::Official).count();
    eprintln!("installed mods with assemblies={with_dlls}, of which code read (facts.types>0)={with_code}");
    let counts = |label: &str, cats: Vec<Category>| {
        let each: Vec<String> = Category::ALL
            .iter()
            .map(|c| format!("{}={}", c.label(), cats.iter().filter(|x| *x == c).count()))
            .collect();
        eprintln!("categories ({label}, n={}): {}", cats.len(), each.join(" "));
    };
    counts("active, as classified", mods.iter().filter_map(|m| library.category_of(m.installed?)).collect());
    counts("all installed", library.categories.clone());

    let key_of = |m: &Installed| m.key();
    let wanted = [
        "sbz.bookcase", "jecrell.doorsexpanded", "mlmlmlm.bluearchivefurniture", "mewn.medievalsigns",
        "brrainz.harmony", "unlimitedhugs.hugslib", "oskarpotocki.vanillafactionsexpanded.core",
        "vanillaexpanded.vfecore", "jaxe.rimhud",
    ];
    let active: HashSet<String> = mods.iter().filter_map(|m| library.get(m.installed?)?.key()).collect();
    for want in wanted {
        let Some((at, m)) = library.ids().find(|(_, m)| key_of(m).as_deref() == Some(want)) else {
            eprintln!("{want}: not installed");
            continue;
        };
        let raw = library.category_of(at).map_or("?", |c| c.label());
        // Same count as `library::build`: every installed mod (active or not, any source) that
        // lists it as a dependency for this game version.
        let dependents: Vec<&Installed> = library
            .installed
            .iter()
            .filter(|o| {
                o.about.as_ref().is_ok_and(|a| {
                    a.dependencies.for_version(&library.game_version).iter().any(|d| id::fold(d) == want)
                })
            })
            .collect();
        let active_deps = dependents.iter().filter(|o| o.key().is_some_and(|k| active.contains(&k))).count();
        let mut unique: Vec<String> = dependents.iter().filter_map(|o| o.key()).collect();
        unique.sort();
        unique.dedup();
        let reason = if m.source == Source::Official {
            "official -> Game".to_string()
        } else if raw == "Loaders" {
            "before_game (loadBefore chain reaches a Ludeon mod) -> Loaders".to_string()
        } else if dependents.len() >= 2 {
            format!("dependents>=2 -> Libraries rule fires ({} copies list it)", dependents.len())
        } else {
            format!("dependents<2; kinds/code decide -> {raw}")
        };
        let first: Vec<String> = dependents
            .iter()
            .take(6)
            .map(|o| format!("{}[{}]", o.display_name(), o.source_label()))
            .collect();
        eprintln!(
            "{want}: category={raw} source={:?} kinds={:?} facts={:?} dependents(all installed copies)={} unique ids={} active={} | {reason} | e.g. {}",
            m.source, m.kinds, m.code.facts, dependents.len(), unique.len(), active_deps, first.join(", ")
        );
    }
}

fn run() -> Result<(), String> {
    let args = args()?;
    let env = &paths::from_environment;
    let home = paths::home(env);
    let (settings_dir, data_root) = paths::app_dirs(paths::Os::running(), &home, env);
    let settings = Settings::read(&settings_dir.join("RimStack").join("settings.json"));
    let data_dir = data_root.join("RimStack");

    // Precedence as the app has it: flag, environment, Settings, detection.
    let from_env = paths::Overrides::from_env();
    let set = |name: &str| settings.get(name).map(PathBuf::from);
    let given = paths::Overrides {
        game: args.game.or(from_env.game).or(set("game")),
        config: args
            .config
            .or(from_env.config)
            .or(set("config"))
            .map(settings_root),
        workshop: args.workshop.or(from_env.workshop).or(set("workshop")),
    };
    let paths = paths::detect(&home, &given);
    let library = library::load(&paths).map_err(|e| e.to_string())?;

    // App's `arranged`: the active list, with hand-filed notes over category and group.
    let notes = Notes::read(&data_dir.join("notes.json"));
    let mut mods: Vec<Mod> = library.active.clone();
    for m in mods.iter_mut() {
        let key = m.installed.and_then(|i| library.get(i)?.key());
        if let Some(note) = key.and_then(|k| notes.get(&k)) {
            m.tags = note.tags.clone();
            m.sub = note.group().to_string();
            if let Some(chosen) = Category::from_kept(&note.category) {
                m.category = chosen;
            }
        }
    }

    // Evidence of which copy won, on stderr and only when asked.
    if std::env::var_os("RIMSTACK_SORTCLI_SOURCES").is_some() {
        let count = |want: Option<Source>| {
            let source = |m: &Mod| m.installed.and_then(|i| library.get(i)).map(|i| i.source);
            mods.iter().filter(|m| source(m) == want).count()
        };
        eprintln!(
            "installed={} official={} local={} workshop={} missing={}",
            library.installed.len(),
            count(Some(Source::Official)),
            count(Some(Source::Local)),
            count(Some(Source::Workshop)),
            count(None)
        );
        for m in mods.iter().filter(|m| m.installed.and_then(|i| library.get(i)).is_some_and(|i| i.source == Source::Workshop)).take(3) {
            eprintln!("workshop copy used: {} -> {}", m.id, library.get(m.installed.unwrap()).unwrap().path.display());
        }
        for m in mods.iter().filter(|m| m.installed.and_then(|i| library.get(i)).is_some_and(|i| i.source == Source::Local)).take(3) {
            eprintln!("local copy used: {} -> {}", m.id, library.get(m.installed.unwrap()).unwrap().path.display());
        }
    }

    if std::env::var_os("RIMSTACK_SORTCLI_SOURCES").is_some() {
        diagnose(&library, &mods, &paths);
    }

    // App's `Detected::of`, with every mod read (the app reads them off-thread first).
    let installed = |m: &Mod| m.installed.and_then(|i| library.get(i));
    let active: HashSet<String> = mods.iter().filter_map(|m| installed(m)?.key()).collect();
    let nothing = infer::Scanned::default();
    let scanned_owned: Vec<Option<infer::Scanned>> = mods
        .par_iter()
        .map(|m| {
            let found = installed(m)?;
            let loaded = loadfolders::folders(&found.path, &library.game_version, &|k| {
                active.contains(k)
            });
            Some(infer::scan(&loaded))
        })
        .collect();
    let scanned: Vec<&infer::Scanned> = scanned_owned
        .iter()
        .map(|s| s.as_ref().unwrap_or(&nothing))
        .collect();
    let names: Vec<String> = mods.iter().map(|m| m.name.clone()).collect();
    let official: Vec<bool> = mods
        .iter()
        .map(|m| installed(m).is_some_and(|i| i.source == Source::Official))
        .collect();
    let roots: Vec<PathBuf> = mods
        .iter()
        .map(|m| installed(m).map(|i| i.path.clone()).unwrap_or_default())
        .collect();
    let from_files = infer::infer(&scanned, &names, &official, &roots);

    let yours = UserRules::read(&data_dir.join("rules.json"));
    let community = community::read(&community::file_in(&data_dir)).unwrap_or(None);
    let sources = Sources {
        yours: &yours,
        community: community
            .as_ref()
            .filter(|_| settings.switch(Switch::CommunityRules)),
        from_files: &from_files,
        hints: settings.switch(Switch::FileHints),
    };

    let before: Vec<String> = mods.iter().map(|m| m.id.clone()).collect();
    let (mods, sorted) = ordering::sorted_by_category(&library, mods, sources);
    let moved = sorted.moved();
    let found = ordering::all_rules(&library, &mods, sources);
    let names: Vec<&str> = mods.iter().map(|m| m.name.as_str()).collect();
    let categories: Vec<Category> = mods.iter().map(|m| m.category).collect();
    let after: Vec<String> = sorted.order.iter().map(|&i| mods[i].id.clone()).collect();

    if std::env::var_os("RIMSTACK_SORTCLI_SOURCES").is_some() {
        let wanted = [
            "sbz.bookcase", "jecrell.doorsexpanded", "mlmlmlm.bluearchivefurniture",
            "mewn.medievalsigns", "brrainz.harmony", "unlimitedhugs.hugslib",
            "oskarpotocki.vanillafactionsexpanded.core", "vanillaexpanded.vfecore", "jaxe.rimhud",
        ];
        for want in wanted {
            let Some(at) = mods.iter().position(|m| m.id.eq_ignore_ascii_case(want)) else {
                eprintln!("{want}: not active");
                continue;
            };
            let raw_of = |i: usize| mods[i].installed.and_then(|x| library.category_of(x)); let raw = raw_of(at).map_or("?", |c| c.label());
            let mut pulled: Vec<String> = found
                .iter()
                .filter(|r| r.first == at && raw_of(r.then) < raw_of(at))
                .map(|r| format!("{:?} before {} [classified {}]", r.kind, mods[r.then].name, raw_of(r.then).map_or("?", |c| c.label())))
                .collect();
            let total = pulled.len();
            pulled.truncate(4);
            eprintln!(
                "{want}: classified={raw} final group={} moved={} rules pulling it up (first=it, then in an earlier category)={total}: {}",
                mods[at].category.label(),
                moved[sorted.order.iter().position(|&o| o == at).unwrap()],
                pulled.join("; ")
            );
        }
    }

    // (id, from, to, why) by new place; places count from 1 as in the app.
    let moves: Vec<(String, usize, usize, String)> = sorted
        .order
        .iter()
        .zip(&moved)
        .enumerate()
        .filter(|(_, (_, moved))| **moved)
        .map(|(now, (&old, _))| {
            let why = sort_reasons::moved(old, &found, &names, &categories);
            (mods[old].id.clone(), old + 1, now + 1, why.text.to_string())
        })
        .collect();

    println!(
        "active={} moved={} rules={}",
        before.len(),
        moves.len(),
        found.len()
    );
    for (id, from, to, why) in &moves {
        println!("{from} -> {to}  {id}  | {why}");
    }

    if let Some(out) = &args.out {
        let source = paths
            .mods_config()
            .ok_or("RimWorld's settings folder was not found")?;
        let same = dunce::canonicalize(out).ok() == dunce::canonicalize(&source).ok();
        if same && out.exists() {
            return Err("--out would overwrite the input ModsConfig.xml".into());
        }
        let text = std::fs::read_to_string(&source)
            .map_err(|e| format!("{}: {e}", source.display()))?;
        let new = modsconfig::with_active(&text, &after).map_err(|e| e.to_string())?;
        if let Some(dir) = out.parent() {
            std::fs::create_dir_all(dir).map_err(|e| format!("{}: {e}", dir.display()))?;
        }
        std::fs::write(out, new).map_err(|e| format!("{}: {e}", out.display()))?;
    }
    if let Some(file) = &args.json {
        let moves: Vec<_> = moves
            .iter()
            .map(|(id, from, to, why)| {
                serde_json::json!({"id": id, "from": from, "to": to, "why": why})
            })
            .collect();
        let report = serde_json::json!({"active": before, "sorted": after, "moves": moves});
        let text = serde_json::to_string_pretty(&report).map_err(|e| e.to_string())?;
        std::fs::write(file, text).map_err(|e| format!("{}: {e}", file.display()))?;
    }
    Ok(())
}

fn main() {
    std::panic::set_hook(Box::new(|_| {}));
    let done = std::panic::catch_unwind(run).unwrap_or_else(|_| Err("internal error".into()));
    if let Err(message) = done {
        eprintln!("error: {}", message.lines().next().unwrap_or("failed"));
        std::process::exit(1);
    }
}
