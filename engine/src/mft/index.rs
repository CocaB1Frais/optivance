//! Reconstruction de l'arborescence à partir des enregistrements MFT.
//!
//! La sortie reprend **exactement** la disposition plate du scan classique (`main.rs`) :
//! racine en 0, enfants d'un dossier contigus et triés par nom, dossiers parcourus en
//! profondeur dans l'ordre des noms. Les deux backends sont ainsi comparables entrée par
//! entrée. Règles reprises du scan classique :
//! - un point de réanalyse est ignoré, ainsi que tout son sous-arbre ;
//! - pour un fichier à plusieurs liens, la première occurrence rencontrée porte la taille,
//!   les suivantes sont marquées `hard_link_duplicate` (tous les chemins restent visibles).
//!
//! Aucune incohérence n'est corrigée silencieusement : elles sont comptées dans
//! `Diagnostics`, et l'appelant décide si le résultat peut être qualifié de complet.
//! Les tailles ne sont pas fournies par l'énumération : elles seront ajoutées plus tard.

#![forbid(unsafe_code)]

use std::collections::hash_map::Entry;
use std::collections::{HashMap, HashSet};

use super::records::UsnRecord;

pub const FILE_ATTRIBUTE_DIRECTORY: u32 = 0x0000_0010;
pub const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0000_0400;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct IndexEntry {
    pub name: String,
    pub file_id: u128,
    pub parent_id: i32,
    pub child_start: i32,
    pub child_count: i32,
    pub is_directory: bool,
    pub hard_link_duplicate: bool,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct Diagnostics {
    /// Enregistrements reçus.
    pub records: u64,
    /// Enregistrements strictement identiques (même fichier, parent et nom) ignorés.
    pub duplicate_records: u64,
    /// Même référence de fichier vue comme dossier et comme fichier.
    pub conflicting_kinds: u64,
    /// Points de réanalyse ignorés (leur sous-arbre devient inaccessible).
    pub reparse_points_skipped: u64,
    /// Références de fichier non rattachées à la racine (parent absent, périmé ou cycle).
    pub unreachable_files: u64,
    /// Parent dont le numéro d'enregistrement existe mais avec une séquence différente.
    pub stale_parent_references: u64,
    /// Liens supplémentaires vers un dossier déjà placé (un dossier n'a qu'un parent).
    pub extra_directory_links: u64,
}

impl Diagnostics {
    /// Vrai si aucune incohérence n'a été relevée (les points de réanalyse ignorés sont
    /// un comportement voulu, partagé avec le scan classique).
    pub fn is_consistent(&self) -> bool {
        self.conflicting_kinds == 0
            && self.unreachable_files == 0
            && self.stale_parent_references == 0
            && self.extra_directory_links == 0
    }
}

#[derive(Debug)]
pub struct MftIndex {
    pub entries: Vec<IndexEntry>,
    pub diagnostics: Diagnostics,
}

/// Numéro d'enregistrement MFT (48 bits de poids faible d'une référence NTFS 64 bits).
pub fn record_number(file_id: u128) -> Option<u64> {
    u64::try_from(file_id).ok().map(|id| id & 0x0000_FFFF_FFFF_FFFF)
}

fn is_directory(attributes: u32) -> bool {
    attributes & FILE_ATTRIBUTE_DIRECTORY != 0
}

/// Construit l'index enraciné sur `root_id` (racine du volume ou dossier analysé).
pub fn build_index(root_id: u128, root_name: &str, records: Vec<UsnRecord>) -> MftIndex {
    let mut diagnostics = Diagnostics { records: records.len() as u64, ..Diagnostics::default() };

    // Attributs retenus par référence : ceux du premier enregistrement vu.
    let mut attributes: HashMap<u128, u32> = HashMap::with_capacity(records.len());
    for record in &records {
        match attributes.entry(record.file_id) {
            Entry::Occupied(known) => {
                if is_directory(*known.get()) != is_directory(record.attributes) {
                    diagnostics.conflicting_kinds += 1;
                }
            }
            Entry::Vacant(slot) => {
                slot.insert(record.attributes);
            }
        }
    }

    // Parents périmés : le numéro d'enregistrement existe, mais pas la référence exacte.
    let known_numbers: HashSet<u64> = attributes.keys().chain(std::iter::once(&root_id)).filter_map(|id| record_number(*id)).collect();
    for record in &records {
        if record.parent_id != root_id
            && !attributes.contains_key(&record.parent_id)
            && record_number(record.parent_id).is_some_and(|number| known_numbers.contains(&number))
        {
            diagnostics.stale_parent_references += 1;
        }
    }

    // Liens par dossier parent ; la racine elle-même n'est jamais rattachée à un parent.
    let links: Vec<UsnRecord> = records.into_iter().filter(|record| record.file_id != root_id).collect();
    let mut children: HashMap<u128, Vec<usize>> = HashMap::new();
    for (position, link) in links.iter().enumerate() {
        children.entry(link.parent_id).or_default().push(position);
    }
    for list in children.values_mut() {
        // Même ordre que le scan classique (nom, ordinal), puis référence pour le déterminisme.
        list.sort_unstable_by(|&left, &right| {
            links[left].name.cmp(&links[right].name).then(links[left].file_id.cmp(&links[right].file_id))
        });
        let before = list.len();
        list.dedup_by(|current, previous| links[*current].name == links[*previous].name && links[*current].file_id == links[*previous].file_id);
        diagnostics.duplicate_records += (before - list.len()) as u64;
    }

    let mut entries = vec![IndexEntry {
        name: root_name.to_owned(),
        file_id: root_id,
        parent_id: -1,
        child_start: 1,
        child_count: 0,
        is_directory: true,
        hard_link_duplicate: false,
    }];
    let mut reached: HashSet<u128> = HashSet::with_capacity(attributes.len());
    let mut placed_directories: HashSet<u128> = HashSet::new();
    reached.insert(root_id);
    placed_directories.insert(root_id);
    let mut pending = vec![(root_id, 0usize)];

    while let Some((directory, directory_index)) = pending.pop() {
        let child_start = entries.len();
        let mut child_directories = Vec::new();
        for &position in children.get(&directory).map(Vec::as_slice).unwrap_or_default() {
            let link = &links[position];
            let link_attributes = attributes[&link.file_id];
            if link_attributes & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
                if reached.insert(link.file_id) {
                    diagnostics.reparse_points_skipped += 1;
                }
                continue;
            }
            let entry_is_directory = is_directory(link_attributes);
            let hard_link_duplicate = if entry_is_directory {
                if !placed_directories.insert(link.file_id) {
                    diagnostics.extra_directory_links += 1;
                    continue;
                }
                reached.insert(link.file_id);
                child_directories.push((link.file_id, entries.len()));
                false
            } else {
                !reached.insert(link.file_id)
            };
            entries.push(IndexEntry {
                name: link.name.clone(),
                file_id: link.file_id,
                parent_id: directory_index as i32,
                child_start: 0,
                child_count: 0,
                is_directory: entry_is_directory,
                hard_link_duplicate,
            });
        }
        entries[directory_index].child_start = child_start as i32;
        entries[directory_index].child_count = (entries.len() - child_start) as i32;
        pending.extend(child_directories.into_iter().rev());
    }

    diagnostics.unreachable_files = attributes.keys().filter(|id| !reached.contains(id)).count() as u64;
    MftIndex { entries, diagnostics }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ROOT: u128 = ref_id(5, 5);
    const DIR: u32 = FILE_ATTRIBUTE_DIRECTORY;
    const FILE: u32 = 0x20;

    /// Référence NTFS : séquence sur 16 bits de poids fort, numéro d'enregistrement sur 48 bits.
    const fn ref_id(record: u64, sequence: u16) -> u128 {
        (((sequence as u64) << 48) | record) as u128
    }

    fn rec(file: u128, parent: u128, attributes: u32, name: &str) -> UsnRecord {
        UsnRecord { file_id: file, parent_id: parent, attributes, name: name.into(), major_version: 2 }
    }

    fn layout(index: &MftIndex) -> Vec<(String, i32, i32, i32, bool)> {
        index
            .entries
            .iter()
            .map(|e| (e.name.clone(), e.parent_id, e.child_start, e.child_count, e.hard_link_duplicate))
            .collect()
    }

    fn row(name: &str, parent: i32, start: i32, count: i32) -> (String, i32, i32, i32, bool) {
        (name.into(), parent, start, count, false)
    }

    #[test]
    fn record_number_keeps_low_48_bits_of_ntfs_reference() {
        assert_eq!(record_number(ref_id(0x40, 3)), Some(0x40));
        assert_eq!(record_number(1u128 << 64), None);
    }

    #[test]
    fn lays_out_entries_in_classic_scan_order() {
        let a = ref_id(30, 1);
        let records = vec![
            rec(ref_id(40, 1), a, FILE, "x.txt"),
            rec(ref_id(31, 1), ROOT, FILE, "c.txt"),
            rec(ref_id(32, 1), ROOT, DIR, "b"),
            rec(a, ROOT, DIR, "a"),
        ];
        let index = build_index(ROOT, "C:\\", records);
        assert_eq!(
            layout(&index),
            vec![
                row("C:\\", -1, 1, 3),
                row("a", 0, 4, 1),
                row("b", 0, 5, 0),
                row("c.txt", 0, 0, 0),
                row("x.txt", 1, 0, 0),
            ]
        );
        assert!(index.entries[1].is_directory && !index.entries[3].is_directory);
        assert_eq!(index.entries[4].file_id, ref_id(40, 1));
        assert!(index.diagnostics.is_consistent());
        assert_eq!(index.diagnostics.records, 4);
    }

    #[test]
    fn keeps_every_hard_link_and_marks_later_occurrences_as_duplicates() {
        let (a, b, shared) = (ref_id(30, 1), ref_id(31, 1), ref_id(50, 1));
        let records = vec![
            rec(a, ROOT, DIR, "a"),
            rec(b, ROOT, DIR, "b"),
            rec(shared, b, FILE, "copie.bin"),
            rec(shared, a, FILE, "original.bin"),
        ];
        let index = build_index(ROOT, "R", records);
        let links: Vec<_> = index.entries.iter().filter(|e| e.file_id == shared).map(|e| (e.name.as_str(), e.hard_link_duplicate)).collect();
        assert_eq!(links, [("original.bin", false), ("copie.bin", true)]);
        assert!(index.diagnostics.is_consistent());
    }

    #[test]
    fn ignores_identical_duplicate_records() {
        let file = ref_id(31, 1);
        let index = build_index(ROOT, "R", vec![rec(file, ROOT, FILE, "f"), rec(file, ROOT, FILE, "f")]);
        assert_eq!(index.entries.len(), 2);
        assert_eq!(index.diagnostics.duplicate_records, 1);
        assert!(index.diagnostics.is_consistent());
    }

    #[test]
    fn skips_reparse_points_with_their_subtree() {
        let junction = ref_id(30, 1);
        let records = vec![
            rec(junction, ROOT, DIR | FILE_ATTRIBUTE_REPARSE_POINT, "jonction"),
            rec(ref_id(31, 1), junction, FILE, "dedans.txt"),
            rec(ref_id(32, 1), ROOT, FILE | FILE_ATTRIBUTE_REPARSE_POINT, "lien"),
            rec(ref_id(33, 1), ROOT, FILE, "normal.txt"),
        ];
        let index = build_index(ROOT, "R", records);
        assert_eq!(layout(&index), vec![row("R", -1, 1, 1), row("normal.txt", 0, 0, 0)]);
        assert_eq!(index.diagnostics.reparse_points_skipped, 2);
        assert_eq!(index.diagnostics.unreachable_files, 1);
    }

    #[test]
    fn counts_records_with_missing_parent_as_unreachable() {
        let index = build_index(ROOT, "R", vec![rec(ref_id(31, 1), ref_id(99, 1), FILE, "orphelin")]);
        assert_eq!(index.entries.len(), 1);
        assert_eq!(index.diagnostics.unreachable_files, 1);
        assert_eq!(index.diagnostics.stale_parent_references, 0);
        assert!(!index.diagnostics.is_consistent());
    }

    #[test]
    fn detects_parent_reference_with_stale_sequence() {
        let records = vec![rec(ref_id(30, 2), ROOT, DIR, "a"), rec(ref_id(31, 1), ref_id(30, 1), FILE, "ancien")];
        let index = build_index(ROOT, "R", records);
        assert_eq!(index.entries.len(), 2);
        assert_eq!(index.diagnostics.stale_parent_references, 1);
        assert_eq!(index.diagnostics.unreachable_files, 1);
    }

    #[test]
    fn terminates_on_directory_cycle_and_reports_it() {
        let (a, b) = (ref_id(30, 1), ref_id(31, 1));
        let index = build_index(ROOT, "R", vec![rec(a, b, DIR, "a"), rec(b, a, DIR, "b")]);
        assert_eq!(index.entries.len(), 1);
        assert_eq!(index.diagnostics.unreachable_files, 2);
    }

    #[test]
    fn ignores_root_self_reference() {
        let index = build_index(ROOT, "R", vec![rec(ROOT, ROOT, DIR, "."), rec(ref_id(31, 1), ROOT, FILE, "f")]);
        assert_eq!(layout(&index), vec![row("R", -1, 1, 1), row("f", 0, 0, 0)]);
        assert!(index.diagnostics.is_consistent());
    }

    #[test]
    fn places_directory_once_when_it_has_two_parent_links() {
        let (a, b, d) = (ref_id(30, 1), ref_id(31, 1), ref_id(32, 1));
        let records = vec![rec(a, ROOT, DIR, "a"), rec(b, ROOT, DIR, "b"), rec(d, a, DIR, "d"), rec(d, b, DIR, "d")];
        let index = build_index(ROOT, "R", records);
        assert_eq!(index.entries.iter().filter(|e| e.file_id == d).count(), 1);
        assert_eq!(index.diagnostics.extra_directory_links, 1);
        assert!(!index.diagnostics.is_consistent());
    }

    #[test]
    fn reports_reference_seen_as_both_directory_and_file() {
        let x = ref_id(30, 1);
        let index = build_index(ROOT, "R", vec![rec(x, ROOT, DIR, "x"), rec(x, ROOT, FILE, "y")]);
        assert_eq!(index.diagnostics.conflicting_kinds, 1);
        assert!(!index.diagnostics.is_consistent());
    }

    #[test]
    fn sorts_children_by_name_then_reference_for_determinism() {
        let records = vec![rec(ref_id(32, 1), ROOT, FILE, "b"), rec(ref_id(31, 1), ROOT, FILE, "B"), rec(ref_id(30, 1), ROOT, FILE, "a")];
        let names: Vec<_> = build_index(ROOT, "R", records).entries.into_iter().skip(1).map(|e| e.name).collect();
        assert_eq!(names, ["B", "a", "b"]);
    }
}
