//! Assemblage de l'index MFT et des tailles en entrées du protocole v2.
//!
//! Les tailles ne figurent pas dans les enregistrements USN : elles sont demandées une
//! seule fois par référence de fichier (`query`), puis réutilisées pour les autres liens.
//! Un échec de lecture n'invente aucune taille : l'entrée vaut 0 et l'échec est compté.

#![forbid(unsafe_code)]

use std::collections::HashMap;

use super::index::MftIndex;
use crate::protocol::{Counters, Entry};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FileSizes {
    pub logical: u64,
    pub allocated: u64,
    /// Nombre de liens physiques déclaré par le système de fichiers.
    pub links: u32,
}

#[derive(Debug, Default, PartialEq, Eq)]
pub struct SizeStats {
    pub lookups: u64,
    pub failures: u64,
    /// Liens déclarés par le système mais absents de l'index (noms non énumérés).
    pub missing_link_names: u64,
}

pub struct Assembled {
    pub entries: Vec<Entry>,
    pub counters: Counters,
    pub stats: SizeStats,
}

pub fn assemble<E>(index: &MftIndex, mut query: impl FnMut(u128) -> Result<FileSizes, E>) -> Assembled {
    let mut stats = SizeStats::default();
    let mut counters = Counters { files: 0, directories: 0, warnings: 0, unique_logical_bytes: 0 };
    let mut sizes: HashMap<u128, Option<FileSizes>> = HashMap::new();
    let mut observed_links: HashMap<u128, u64> = HashMap::new();
    let mut entries = Vec::with_capacity(index.entries.len());

    for source in &index.entries {
        let (logical, allocated) = if source.is_directory {
            counters.directories += 1;
            (0, 0)
        } else {
            counters.files += 1;
            *observed_links.entry(source.file_id).or_default() += 1;
            let known = *sizes.entry(source.file_id).or_insert_with(|| {
                stats.lookups += 1;
                let result = query(source.file_id).ok();
                if result.is_none() {
                    stats.failures += 1;
                }
                result
            });
            known.map_or((0, 0), |found| (found.logical, found.allocated))
        };
        let counted = !source.is_directory && !source.hard_link_duplicate;
        if counted {
            counters.unique_logical_bytes = counters.unique_logical_bytes.saturating_add(logical);
        }
        entries.push(Entry {
            name: source.name.clone(),
            parent_id: source.parent_id,
            child_start: source.child_start,
            child_count: source.child_count,
            is_directory: source.is_directory,
            logical_bytes: logical,
            allocated_bytes: allocated,
            unique_logical_bytes: if counted { logical } else { 0 },
            unique_allocated_bytes: if counted { allocated } else { 0 },
            hard_link_duplicate: source.hard_link_duplicate,
        });
    }

    for (file_id, observed) in observed_links {
        if let Some(Some(found)) = sizes.get(&file_id) {
            stats.missing_link_names += u64::from(found.links).saturating_sub(observed);
        }
    }
    Assembled { entries, counters, stats }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mft::index::{build_index, FILE_ATTRIBUTE_DIRECTORY};
    use crate::mft::records::UsnRecord;

    const ROOT: u128 = 5;

    fn rec(file: u128, parent: u128, attributes: u32, name: &str) -> UsnRecord {
        UsnRecord { file_id: file, parent_id: parent, attributes, name: name.into(), major_version: 2 }
    }

    fn sample() -> MftIndex {
        build_index(
            ROOT,
            "C:\\",
            vec![
                rec(30, ROOT, FILE_ATTRIBUTE_DIRECTORY, "a"),
                rec(40, 30, 0x20, "lien1"),
                rec(40, ROOT, 0x20, "lien2"),
                rec(41, ROOT, 0x20, "seul"),
            ],
        )
    }

    #[test]
    fn queries_each_file_once_and_shares_sizes_with_later_links() {
        let mut calls = Vec::new();
        let assembled = assemble(&sample(), |id| {
            calls.push(id);
            Ok::<_, ()>(FileSizes { logical: id as u64, allocated: 4096, links: if id == 40 { 2 } else { 1 } })
        });
        calls.sort_unstable();
        assert_eq!(calls, [40, 41]);
        let rows: Vec<_> = assembled
            .entries
            .iter()
            .map(|e| (e.name.as_str(), e.logical_bytes, e.unique_logical_bytes, e.unique_allocated_bytes, e.hard_link_duplicate))
            .collect();
        assert_eq!(
            rows,
            [
                ("C:\\", 0, 0, 0, false),
                ("a", 0, 0, 0, false),
                ("lien2", 40, 40, 4096, false),
                ("seul", 41, 41, 4096, false),
                ("lien1", 40, 0, 0, true),
            ]
        );
        assert_eq!(assembled.stats, SizeStats { lookups: 2, failures: 0, missing_link_names: 0 });
    }

    #[test]
    fn keeps_flat_layout_from_index() {
        let index = sample();
        let assembled = assemble(&index, |_| Ok::<_, ()>(FileSizes { logical: 1, allocated: 1, links: 1 }));
        for (built, source) in assembled.entries.iter().zip(&index.entries) {
            assert_eq!(
                (built.parent_id, built.child_start, built.child_count, built.is_directory),
                (source.parent_id, source.child_start, source.child_count, source.is_directory)
            );
        }
    }

    #[test]
    fn counts_files_directories_and_unique_bytes_like_classic_scan() {
        let assembled = assemble(&sample(), |id| Ok::<_, ()>(FileSizes { logical: id as u64, allocated: 0, links: 1 }));
        let counters = &assembled.counters;
        assert_eq!((counters.files, counters.directories, counters.unique_logical_bytes), (3, 2, 81));
    }

    #[test]
    fn failed_lookup_yields_zero_sizes_and_is_counted() {
        let assembled = assemble(&sample(), |id| if id == 41 { Err("accès refusé") } else { Ok(FileSizes { logical: 7, allocated: 8, links: 2 }) });
        let seul = assembled.entries.iter().find(|e| e.name == "seul").unwrap();
        assert_eq!((seul.logical_bytes, seul.allocated_bytes, seul.unique_logical_bytes), (0, 0, 0));
        assert_eq!(assembled.stats.failures, 1);
    }

    #[test]
    fn reports_hard_link_names_missing_from_index() {
        let assembled = assemble(&sample(), |id| Ok::<_, ()>(FileSizes { logical: 1, allocated: 1, links: if id == 41 { 3 } else { 2 } }));
        assert_eq!(assembled.stats.missing_link_names, 2);
    }
}
