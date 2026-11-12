//! Collecte des métadonnées par référence de fichier, avant reconstruction de l'index.
//!
//! - Tailles et nombre de liens : une seule ouverture par fichier.
//! - Points de réanalyse : l'attribut seul ne suffit pas. Seuls les « name surrogates »
//!   (liens symboliques, jonctions, points de montage : ils désignent un autre
//!   emplacement) sont ignorés. Les autres (fichiers compressés par Windows, fichiers
//!   cloud, déduplication…) sont de vrais fichiers locaux : l'attribut est retiré pour
//!   qu'ils soient indexés normalement. Type inconnu ou illisible : ignoré et compté.

#![forbid(unsafe_code)]

use std::collections::{BTreeMap, HashMap, HashSet};

use super::assemble::FileSizes;
use super::index::{FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_REPARSE_POINT};
use super::records::UsnRecord;

/// Bit « name surrogate » des tags de réanalyse (IsReparseTagNameSurrogate).
pub const REPARSE_TAG_NAME_SURROGATE: u32 = 0x2000_0000;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FileInfo {
    pub sizes: FileSizes,
    /// Renseigné uniquement si demandé (enregistrement portant l'attribut de réanalyse).
    pub reparse_tag: Option<u32>,
}

#[derive(Debug, Default, PartialEq, Eq)]
pub struct ReparseStats {
    /// Nombre de références par tag (aucun nom de fichier).
    pub by_tag: BTreeMap<u32, u64>,
    /// Points de réanalyse indexés comme fichiers/dossiers ordinaires.
    pub followed: u64,
    /// Name surrogates ignorés, comme le scan classique.
    pub skipped_surrogates: u64,
}

#[derive(Debug, Default)]
pub struct Collected {
    pub sizes: HashMap<u128, FileSizes>,
    pub reparse: ReparseStats,
    pub queries: u64,
    /// Références dont les métadonnées n'ont pas pu être lues.
    pub failures: u64,
}

/// `query(file_id, want_reparse_tag)` ouvre la référence et lit ses métadonnées.
pub fn collect<E>(records: &mut [UsnRecord], mut query: impl FnMut(u128, bool) -> Result<FileInfo, E>) -> Collected {
    let mut collected = Collected::default();
    let mut seen: HashSet<u128> = HashSet::with_capacity(records.len());
    let mut followed: HashSet<u128> = HashSet::new();

    for record in records.iter() {
        let is_directory = record.attributes & FILE_ATTRIBUTE_DIRECTORY != 0;
        let is_reparse = record.attributes & FILE_ATTRIBUTE_REPARSE_POINT != 0;
        if (is_directory && !is_reparse) || !seen.insert(record.file_id) {
            continue;
        }
        collected.queries += 1;
        let Ok(info) = query(record.file_id, is_reparse) else {
            collected.failures += 1;
            continue;
        };
        if is_reparse {
            let Some(tag) = info.reparse_tag else {
                collected.failures += 1;
                continue;
            };
            *collected.reparse.by_tag.entry(tag).or_default() += 1;
            if tag & REPARSE_TAG_NAME_SURROGATE != 0 {
                collected.reparse.skipped_surrogates += 1;
                continue;
            }
            collected.reparse.followed += 1;
            followed.insert(record.file_id);
        }
        if !is_directory {
            collected.sizes.insert(record.file_id, info.sizes);
        }
    }

    for record in records.iter_mut().filter(|record| followed.contains(&record.file_id)) {
        record.attributes &= !FILE_ATTRIBUTE_REPARSE_POINT;
    }
    collected
}

#[cfg(test)]
mod tests {
    use super::*;

    const FILE: u32 = 0x20;
    const DIR: u32 = FILE_ATTRIBUTE_DIRECTORY;
    const REPARSE: u32 = FILE_ATTRIBUTE_REPARSE_POINT;
    const TAG_WOF: u32 = 0x8000_0017;
    const TAG_SYMLINK: u32 = 0xA000_000C;
    const TAG_MOUNT_POINT: u32 = 0xA000_0003;

    fn rec(file: u128, attributes: u32) -> UsnRecord {
        UsnRecord { file_id: file, parent_id: 5, attributes, name: format!("n{file}"), major_version: 2 }
    }

    fn info(logical: u64, tag: Option<u32>) -> FileInfo {
        FileInfo { sizes: FileSizes { logical, allocated: logical, links: 1 }, reparse_tag: tag }
    }

    #[test]
    fn queries_each_file_once_and_never_plain_directories() {
        let mut records = vec![rec(10, FILE), rec(10, FILE), rec(11, DIR), rec(12, FILE)];
        let mut calls = Vec::new();
        let collected = collect(&mut records, |id, want_tag| {
            calls.push((id, want_tag));
            Ok::<_, ()>(info(id as u64, None))
        });
        calls.sort_unstable();
        assert_eq!(calls, [(10, false), (12, false)]);
        assert_eq!(collected.queries, 2);
        assert_eq!(collected.sizes.get(&12).map(|s| s.logical), Some(12));
    }

    #[test]
    fn follows_non_surrogate_reparse_points_as_ordinary_entries() {
        let mut records = vec![rec(10, FILE | REPARSE), rec(11, DIR | REPARSE)];
        let collected = collect(&mut records, |id, want_tag| {
            assert!(want_tag);
            Ok::<_, ()>(info(7, Some(if id == 10 { TAG_WOF } else { 0x9000_001A })))
        });
        assert_eq!(records.iter().map(|r| r.attributes & REPARSE).collect::<Vec<_>>(), [0, 0]);
        assert_eq!(records[1].attributes, DIR);
        assert_eq!(collected.reparse.followed, 2);
        assert_eq!(collected.reparse.by_tag.get(&TAG_WOF), Some(&1));
        assert_eq!(collected.sizes.get(&10).map(|s| s.logical), Some(7));
        assert!(!collected.sizes.contains_key(&11));
    }

    #[test]
    fn keeps_name_surrogates_marked_for_skipping() {
        let mut records = vec![rec(10, FILE | REPARSE), rec(11, DIR | REPARSE)];
        let collected = collect(&mut records, |id, _| Ok::<_, ()>(info(0, Some(if id == 10 { TAG_SYMLINK } else { TAG_MOUNT_POINT }))));
        assert!(records.iter().all(|r| r.attributes & REPARSE != 0));
        assert_eq!((collected.reparse.skipped_surrogates, collected.reparse.followed), (2, 0));
    }

    #[test]
    fn failed_query_is_counted_and_keeps_reparse_point_skipped() {
        let mut records = vec![rec(10, FILE | REPARSE), rec(12, FILE)];
        let collected = collect(&mut records, |_, _| Err("refusé"));
        assert_eq!(collected.failures, 2);
        assert_ne!(records[0].attributes & REPARSE, 0);
        assert!(collected.sizes.is_empty());
    }

    #[test]
    fn missing_tag_on_reparse_point_keeps_it_skipped() {
        let mut records = vec![rec(10, FILE | REPARSE)];
        let collected = collect(&mut records, |_, _| Ok::<_, ()>(info(3, None)));
        assert_ne!(records[0].attributes & REPARSE, 0);
        assert_eq!(collected.failures, 1);
    }
}
