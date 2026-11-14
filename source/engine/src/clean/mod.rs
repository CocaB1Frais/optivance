//! Nettoyage approfondi exécuté par le composant de session élevé.
//!
//! - Seuls les identifiants de catégorie ci-dessous sont acceptés ; les emplacements sont fixés
//!   ici (aucun chemin n'est reçu de l'interface) et calculés à partir du dossier Windows réel,
//!   jamais des variables d'environnement.
//! - Aucun point d'analyse (lien, jonction) n'est suivi ni supprimé.
//! - Chaque suppression porte sur un handle dont le chemin final est revérifié
//!   (voir `sys::delete_file_within`) ; âge minimal revérifié au moment de la suppression.
//! - Les fichiers ouverts ou protégés sont ignorés et comptés.

pub mod sys;

use std::fmt::Write as _;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use crate::protocol::push_json_string;
use sys::Removal;

const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
const HOUR_FILETIME: u64 = 36_000_000_000;

#[derive(Clone, Copy)]
enum Base {
    Windows,
    SystemDrive,
    ProgramData,
}

#[derive(Clone, Copy)]
struct Target {
    base: Base,
    relative: &'static str,
    pattern: &'static str,
    recursive: bool,
    minimum_age_hours: u64,
    whole_content: bool,
}

const fn content(base: Base, relative: &'static str, minimum_age_hours: u64) -> Target {
    Target { base, relative, pattern: "*", recursive: true, minimum_age_hours, whole_content: true }
}

const fn files(base: Base, relative: &'static str, pattern: &'static str, recursive: bool, minimum_age_hours: u64) -> Target {
    Target { base, relative, pattern, recursive, minimum_age_hours, whole_content: false }
}

/// Copie des catégories approfondies de `CleaningCatalog` (C#), mêmes identifiants.
const CATEGORIES: &[(&str, &[Target])] = &[
    ("windows-temp", &[content(Base::Windows, "Temp", 24)]),
    ("windows-update", &[content(Base::Windows, r"SoftwareDistribution\Download", 24)]),
    (
        "delivery-optimization",
        &[content(Base::Windows, r"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", 0)],
    ),
    (
        "memory-dumps",
        &[
            files(Base::Windows, "", "MEMORY.DMP", false, 0),
            content(Base::Windows, "Minidump", 0),
            files(Base::Windows, "LiveKernelReports", "*.dmp", true, 0),
        ],
    ),
    (
        "system-crash",
        &[content(Base::ProgramData, r"Microsoft\Windows\WER\ReportArchive", 0), content(Base::ProgramData, r"Microsoft\Windows\WER\ReportQueue", 0)],
    ),
    (
        "windows-logs",
        &[
            files(Base::Windows, r"Logs\CBS", "*.log", false, 168),
            files(Base::Windows, r"Logs\DISM", "*.log", false, 168),
            files(Base::Windows, "Panther", "*.log", false, 168),
        ],
    ),
    ("windows-old", &[content(Base::SystemDrive, "Windows.old", 0)]),
];

/// Identifiant canonique si `id` est une catégorie approfondie connue.
pub fn category_id(id: &str) -> Option<&'static str> {
    CATEGORIES.iter().find(|(known, _)| *known == id).map(|(known, _)| *known)
}

#[derive(Debug, Default, PartialEq, Eq)]
pub struct Report {
    pub files: u64,
    pub bytes: u64,
    pub deleted: u64,
    pub freed: u64,
    pub skipped: u64,
    pub refused: u64,
    pub cancelled: bool,
}

impl Report {
    pub fn to_json(&self, category: &str, run: bool) -> String {
        let mut line = String::from("{\"v\":3,\"type\":\"clean\",\"category\":");
        push_json_string(&mut line, category);
        let _ = write!(
            line,
            ",\"mode\":\"{}\",\"state\":\"{}\",\"files\":{},\"bytes\":{},\"deleted\":{},\"freed\":{},\"skipped\":{},\"refused\":{}}}",
            if run { "run" } else { "analyze" },
            if self.cancelled { "cancelled" } else { "completed" },
            self.files,
            self.bytes,
            self.deleted,
            self.freed,
            self.skipped,
            self.refused
        );
        line
    }
}

/// Correspondance insensible à la casse avec `*` et `?`.
pub fn matches_pattern(pattern: &str, name: &str) -> bool {
    let pattern: Vec<char> = pattern.to_uppercase().chars().collect();
    let name: Vec<char> = name.to_uppercase().chars().collect();
    let (mut p, mut n, mut star, mut mark) = (0usize, 0usize, None::<usize>, 0usize);
    while n < name.len() {
        if p < pattern.len() && (pattern[p] == '?' || pattern[p] == name[n]) {
            p += 1;
            n += 1;
        } else if p < pattern.len() && pattern[p] == '*' {
            star = Some(p);
            mark = n;
            p += 1;
        } else if let Some(star) = star {
            p = star + 1;
            mark += 1;
            n = mark;
        } else {
            return false;
        }
    }
    pattern[p..].iter().all(|character| *character == '*')
}

fn now_filetime() -> u64 {
    let since_unix = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default();
    (since_unix.as_secs() + 11_644_473_600) * 10_000_000 + u64::from(since_unix.subsec_nanos() / 100)
}

/// Emplacements résolus : (dossier, chemin final attendu en majuscules).
struct Location {
    folder: PathBuf,
    target: Target,
}

fn resolve(targets: &[Target]) -> Vec<Location> {
    let Ok(windows) = sys::system_windows_directory() else {
        return Vec::new();
    };
    // « C:\Windows » → « C: ».
    let system_drive = windows.get(..2).unwrap_or("C:").to_string();
    targets
        .iter()
        .map(|target| {
            let base = match target.base {
                Base::Windows => PathBuf::from(&windows),
                Base::SystemDrive => PathBuf::from(format!("{system_drive}\\")),
                Base::ProgramData => PathBuf::from(format!("{system_drive}\\ProgramData")),
            };
            let folder = if target.relative.is_empty() { base } else { base.join(target.relative) };
            Location { folder, target: *target }
        })
        .collect()
}

/// Analyse (`run == false`) ou nettoyage d'une catégorie. `keep_going` est appelé
/// régulièrement : `false` interrompt (annulation de l'interface).
pub fn clean_category(id: &str, run: bool, keep_going: &mut dyn FnMut() -> bool) -> Report {
    let mut report = Report::default();
    let Some((_, targets)) = CATEGORIES.iter().find(|(known, _)| *known == id) else {
        return report;
    };
    let now = now_filetime();
    let mut visited = 0u64;
    for location in resolve(targets) {
        // La racine elle-même : ni lien ni redirection (son chemin final doit être le chemin attendu).
        let Ok(root_final) = sys::canonical_folder(&location.folder) else {
            continue;
        };
        let expected = format!(r"\\?\{}", location.folder.display()).to_uppercase();
        if root_final.trim_end_matches('\\') != expected.trim_end_matches('\\') {
            report.refused += 1;
            continue;
        }
        let root_prefix = format!("{}\\", root_final.trim_end_matches('\\'));
        let cutoff = now.saturating_sub(location.target.minimum_age_hours * HOUR_FILETIME);
        walk(&location.folder, &location.target, cutoff, &root_prefix, run, &mut report, &mut visited, keep_going);
        if report.cancelled {
            break;
        }
        if run && location.target.whole_content {
            remove_empty_folders(&location.folder, true);
        }
    }
    report
}

#[allow(clippy::too_many_arguments)]
fn walk(
    folder: &Path,
    target: &Target,
    cutoff: u64,
    root_prefix: &str,
    run: bool,
    report: &mut Report,
    visited: &mut u64,
    keep_going: &mut dyn FnMut() -> bool,
) {
    use std::os::windows::fs::MetadataExt;
    let Ok(entries) = std::fs::read_dir(folder) else {
        return;
    };
    for entry in entries.flatten() {
        *visited += 1;
        if *visited % 1024 == 0 && !keep_going() {
            report.cancelled = true;
            return;
        }
        // Métadonnées de l'entrée elle-même (aucun lien suivi).
        let Ok(metadata) = entry.metadata() else {
            continue;
        };
        if metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
            continue;
        }
        let path = entry.path();
        if metadata.is_dir() {
            if target.recursive {
                walk(&path, target, cutoff, root_prefix, run, report, visited, keep_going);
                if report.cancelled {
                    return;
                }
            }
            continue;
        }
        let name = entry.file_name();
        if !matches_pattern(target.pattern, &name.to_string_lossy()) {
            continue;
        }
        if metadata.last_write_time() > cutoff || metadata.creation_time() > cutoff {
            continue;
        }
        report.files += 1;
        report.bytes += metadata.len();
        if run {
            match sys::delete_file_within(&path, root_prefix) {
                Removal::Deleted => {
                    report.deleted += 1;
                    report.freed += metadata.len();
                }
                Removal::Refused => report.refused += 1,
                Removal::Failed => report.skipped += 1,
            }
        }
    }
}

/// Retire les dossiers vides (jamais la racine, jamais un point d'analyse).
fn remove_empty_folders(folder: &Path, is_root: bool) {
    use std::os::windows::fs::MetadataExt;
    let Ok(metadata) = std::fs::symlink_metadata(folder) else {
        return;
    };
    if metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 || !metadata.is_dir() {
        return;
    }
    if let Ok(entries) = std::fs::read_dir(folder) {
        for entry in entries.flatten() {
            if entry.metadata().is_ok_and(|child| child.is_dir()) {
                remove_empty_folders(&entry.path(), false);
            }
        }
    }
    if !is_root {
        // Échoue sans effet si le dossier n'est pas vide.
        let _ = std::fs::remove_dir(folder);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn known_categories_only() {
        assert_eq!(category_id("windows-temp"), Some("windows-temp"));
        assert_eq!(category_id("windows-old"), Some("windows-old"));
        for id in ["", "user-temp", "WINDOWS-TEMP", "windows-temp ", "..\\x", "C:\\Windows"] {
            assert_eq!(category_id(id), None, "{id:?}");
        }
    }

    #[test]
    fn patterns() {
        assert!(matches_pattern("*", "a.bin"));
        assert!(matches_pattern("*.log", "CBS.LOG"));
        assert!(matches_pattern("MEMORY.DMP", "memory.dmp"));
        assert!(matches_pattern("thumbcache_*.db", "thumbcache_256.db"));
        assert!(!matches_pattern("*.log", "cbs.log.bak"));
        assert!(!matches_pattern("MEMORY.DMP", "MEMORY.DMP.old"));
        assert!(matches_pattern("a?c", "abc") && !matches_pattern("a?c", "ac"));
    }

    #[test]
    fn report_json() {
        let report = Report { files: 2, bytes: 10, deleted: 1, freed: 4, skipped: 1, refused: 0, cancelled: false };
        assert_eq!(
            report.to_json("windows-temp", true),
            "{\"v\":3,\"type\":\"clean\",\"category\":\"windows-temp\",\"mode\":\"run\",\"state\":\"completed\",\"files\":2,\"bytes\":10,\"deleted\":1,\"freed\":4,\"skipped\":1,\"refused\":0}"
        );
    }

    #[test]
    fn deletion_refuses_paths_outside_the_root_and_junctions() {
        let base = std::env::temp_dir().join(format!("optivance-clean-rs-{}", std::process::id()));
        let inside = base.join("racine");
        let outside = base.join("dehors");
        std::fs::create_dir_all(&inside).unwrap();
        std::fs::create_dir_all(&outside).unwrap();
        std::fs::write(inside.join("a.tmp"), b"x").unwrap();
        std::fs::write(outside.join("precieux.txt"), b"x").unwrap();
        let junction = inside.join("lien");
        let status = std::process::Command::new("cmd")
            .args(["/c", "mklink", "/J"])
            .arg(&junction)
            .arg(&outside)
            .output()
            .unwrap();
        assert!(status.status.success());

        let root = format!("{}\\", sys::canonical_folder(&inside).unwrap());
        // Fichier atteint à travers la jonction : chemin final hors racine → refusé.
        assert_eq!(sys::delete_file_within(&junction.join("precieux.txt"), &root), Removal::Refused);
        assert!(outside.join("precieux.txt").exists());
        // La jonction elle-même : point d'analyse → refusé.
        assert_eq!(sys::delete_file_within(&junction, &root), Removal::Refused);
        // Fichier légitime : supprimé.
        assert_eq!(sys::delete_file_within(&inside.join("a.tmp"), &root), Removal::Deleted);
        assert!(!inside.join("a.tmp").exists());

        std::fs::remove_dir(&junction).unwrap();
        std::fs::remove_dir_all(&base).unwrap();
    }
}
