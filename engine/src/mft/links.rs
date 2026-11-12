//! Complétion des noms de hardlink.
//!
//! Mesuré le 13/11/2026 sur C: : `FSCTL_ENUM_USN_DATA` ne renvoie qu'un nom par fichier
//! (47 043 noms déclarés par NTFS absents de l'énumération). Pour chaque fichier ayant
//! plusieurs liens, `list_names(chemin)` (dans le helper : `FindFirstFileNameW`) renvoie
//! tous ses noms relatifs à la racine du volume (« \dossier\nom »). Chaque nom manquant
//! devient un enregistrement supplémentaire, rattaché à son dossier parent retrouvé
//! dans l'énumération. Un nom dont le dossier est introuvable est compté, jamais deviné.

#![forbid(unsafe_code)]

use std::collections::{HashMap, HashSet};

use super::assemble::FileSizes;
use super::index::{FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_REPARSE_POINT};
use super::records::UsnRecord;

#[derive(Debug, Default, PartialEq, Eq)]
pub struct LinkStats {
    /// Fichiers à plusieurs liens interrogés.
    pub files_queried: u64,
    pub names_added: u64,
    /// Noms renvoyés dont le dossier parent n'a pas été retrouvé dans l'énumération.
    pub unresolved_names: u64,
    /// Fichiers dont le chemin n'a pas pu être construit ou dont la requête a échoué.
    pub failures: u64,
}

pub fn complete_hard_links<E>(
    records: &mut Vec<UsnRecord>,
    root_id: u128,
    sizes: &HashMap<u128, FileSizes>,
    mut list_names: impl FnMut(&str) -> Result<Vec<String>, E>,
) -> LinkStats {
    let mut stats = LinkStats::default();
    let mut directories: HashMap<u128, (u128, String)> = HashMap::new();
    let mut children: HashMap<(u128, String), u128> = HashMap::new();
    for record in records.iter().filter(|record| record.attributes & FILE_ATTRIBUTE_DIRECTORY != 0) {
        directories.entry(record.file_id).or_insert_with(|| (record.parent_id, record.name.clone()));
        children.entry((record.parent_id, record.name.clone())).or_insert(record.file_id);
    }

    let mut queried = HashSet::new();
    let candidates: Vec<usize> = (0..records.len())
        .filter(|&position| {
            let record = &records[position];
            record.attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT) == 0
                && sizes.get(&record.file_id).is_some_and(|found| found.links > 1)
                && queried.insert(record.file_id)
        })
        .collect();

    for position in candidates {
        let source = records[position].clone();
        let Some(path) = volume_relative_path(&directories, root_id, source.parent_id, &source.name) else {
            stats.failures += 1;
            continue;
        };
        stats.files_queried += 1;
        let Ok(names) = list_names(&path) else {
            stats.failures += 1;
            continue;
        };
        for name in names {
            let mut components: Vec<&str> = name.split('\\').filter(|component| !component.is_empty()).collect();
            let Some(leaf) = components.pop() else {
                stats.unresolved_names += 1;
                continue;
            };
            let parent = components.iter().try_fold(root_id, |current, component| children.get(&(current, (*component).to_owned())).copied());
            match parent {
                Some(parent) if parent == source.parent_id && leaf == source.name => {}
                Some(parent) => {
                    records.push(UsnRecord { parent_id: parent, name: leaf.to_owned(), ..source.clone() });
                    stats.names_added += 1;
                }
                None => stats.unresolved_names += 1,
            }
        }
    }
    stats
}

/// « \a\b\nom » ; `None` si la chaîne des parents n'atteint pas la racine (orphelin, cycle).
fn volume_relative_path(directories: &HashMap<u128, (u128, String)>, root_id: u128, parent: u128, name: &str) -> Option<String> {
    let mut components = vec![name];
    let mut current = parent;
    for _ in 0..=directories.len() {
        if current == root_id {
            components.reverse();
            return Some(components.iter().fold(String::new(), |path, component| path + "\\" + component));
        }
        let (next, directory_name) = directories.get(&current)?;
        components.push(directory_name);
        current = *next;
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    const ROOT: u128 = 5;
    const DIR: u32 = FILE_ATTRIBUTE_DIRECTORY;
    const FILE: u32 = 0x20;

    fn rec(file: u128, parent: u128, attributes: u32, name: &str) -> UsnRecord {
        UsnRecord { file_id: file, parent_id: parent, attributes, name: name.into(), major_version: 2 }
    }

    fn sizes(entries: &[(u128, u32)]) -> HashMap<u128, FileSizes> {
        entries.iter().map(|&(id, links)| (id, FileSizes { logical: 1, allocated: 1, links })).collect()
    }

    fn tree() -> Vec<UsnRecord> {
        vec![
            rec(30, ROOT, DIR, "a"),
            rec(31, ROOT, DIR, "b"),
            rec(32, 30, DIR, "c"),
            rec(40, 32, FILE, "x.dll"),
            rec(41, ROOT, FILE, "seul.txt"),
        ]
    }

    #[test]
    fn adds_names_missing_from_enumeration_under_their_real_parent() {
        let mut records = tree();
        let mut asked = Vec::new();
        let stats = complete_hard_links(&mut records, ROOT, &sizes(&[(40, 2), (41, 1)]), |path| {
            asked.push(path.to_owned());
            Ok::<_, ()>(vec!["\\a\\c\\x.dll".to_owned(), "\\b\\copie.dll".to_owned()])
        });
        assert_eq!(asked, ["\\a\\c\\x.dll"]);
        assert_eq!(records.last(), Some(&rec(40, 31, FILE, "copie.dll")));
        assert_eq!(stats, LinkStats { files_queried: 1, names_added: 1, unresolved_names: 0, failures: 0 });
    }

    #[test]
    fn adds_second_name_in_same_directory() {
        let mut records = tree();
        let stats = complete_hard_links(&mut records, ROOT, &sizes(&[(41, 2)]), |_| {
            Ok::<_, ()>(vec!["\\seul.txt".to_owned(), "\\alias.txt".to_owned()])
        });
        assert_eq!(records.last(), Some(&rec(41, ROOT, FILE, "alias.txt")));
        assert_eq!(stats.names_added, 1);
    }

    #[test]
    fn skips_files_with_a_single_link_or_unknown_size() {
        let mut records = tree();
        let stats = complete_hard_links(&mut records, ROOT, &sizes(&[(40, 1)]), |_| -> Result<Vec<String>, ()> { panic!("aucun appel attendu") });
        assert_eq!((stats.files_queried, records.len()), (0, 5));
    }

    #[test]
    fn counts_names_whose_parent_directory_is_unknown() {
        let mut records = tree();
        let stats = complete_hard_links(&mut records, ROOT, &sizes(&[(40, 2)]), |_| {
            Ok::<_, ()>(vec!["\\a\\c\\x.dll".to_owned(), "\\inconnu\\y.dll".to_owned()])
        });
        assert_eq!((stats.names_added, stats.unresolved_names, records.len()), (0, 1, 5));
    }

    #[test]
    fn counts_failed_queries_and_unbuildable_paths() {
        let mut records = tree();
        records.push(rec(42, 99, FILE, "orphelin"));
        let stats = complete_hard_links(&mut records, ROOT, &sizes(&[(40, 2), (42, 2)]), |_| Err("refusé"));
        assert_eq!((stats.files_queried, stats.failures), (1, 2));
    }

    #[test]
    fn ignores_skipped_reparse_points() {
        let mut records = tree();
        records[3].attributes |= FILE_ATTRIBUTE_REPARSE_POINT;
        let stats = complete_hard_links(&mut records, ROOT, &sizes(&[(40, 2)]), |_| -> Result<Vec<String>, ()> { panic!("aucun appel attendu") });
        assert_eq!(stats.files_queried, 0);
    }
}
