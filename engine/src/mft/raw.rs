//! Lecture directe de la `$MFT` NTFS (backend MFT principal, choix du propriétaire du 13/11/2026).
//!
//! Le volume est lu en lecture seule ; chaque enregistrement FILE est décodé ici :
//! corrections de secteur (update sequence), `$STANDARD_INFORMATION` (attributs, date),
//! `$FILE_NAME` (tous les liens physiques, noms DOS 8.3 exclus), `$DATA` sans nom
//! (taille logique et allouée), `$REPARSE_POINT` (tag). Les enregistrements d'extension
//! sont rattachés à leur enregistrement de base, ce qui couvre les listes d'attributs
//! sans avoir à les lire. Les flux nommés (ADS) ne sont pas comptés (règle produit).
//!
//! Tout est strict : un enregistrement incohérent est compté comme corrompu, jamais
//! interprété au hasard. Aucun appel système : la lecture est fournie par `read_at`.

#![forbid(unsafe_code)]

use crate::fasthash::FastMap as HashMap;
use std::fmt;

use super::assemble::FileSizes;
use super::index::{FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_REPARSE_POINT};
use super::records::UsnRecord;

pub const ROOT_RECORD: u64 = 5;
/// Enregistrements réservés aux métafichiers NTFS ($MFT, $LogFile, $Extend…).
const FIRST_USER_RECORD: u64 = 16;
const RECORD_NUMBER_MASK: u64 = 0x0000_FFFF_FFFF_FFFF;

#[derive(Debug, PartialEq, Eq)]
pub enum RawError {
    NotNtfs,
    InvalidBoot(&'static str),
    BadMftRecord(&'static str),
    BadRuns,
    Unsupported(&'static str),
    Io(String),
    Cancelled,
}

impl fmt::Display for RawError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::NotNtfs => write!(f, "le secteur de démarrage n'est pas NTFS"),
            Self::InvalidBoot(why) => write!(f, "secteur de démarrage NTFS invalide : {why}"),
            Self::BadMftRecord(why) => write!(f, "enregistrement $MFT illisible : {why}"),
            Self::BadRuns => write!(f, "liste de clusters de la $MFT invalide"),
            Self::Unsupported(why) => write!(f, "configuration $MFT non prise en charge : {why}"),
            Self::Io(error) => write!(f, "lecture du volume impossible : {error}"),
            Self::Cancelled => write!(f, "lecture de la $MFT annulée"),
        }
    }
}

impl std::error::Error for RawError {}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BootSector {
    pub bytes_per_sector: u64,
    pub cluster_size: u64,
    pub mft_lcn: u64,
    pub record_size: usize,
}

impl BootSector {
    pub fn parse(sector: &[u8]) -> Result<Self, RawError> {
        if sector.len() < 512 {
            return Err(RawError::InvalidBoot("secteur trop court"));
        }
        if &sector[3..11] != b"NTFS    " {
            return Err(RawError::NotNtfs);
        }
        let bytes_per_sector = u64::from(u16_at(sector, 0x0B));
        if !matches!(bytes_per_sector, 512 | 1024 | 2048 | 4096) {
            return Err(RawError::InvalidBoot("taille de secteur"));
        }
        let sectors_per_cluster = match sector[0x0D] {
            0 => return Err(RawError::InvalidBoot("secteurs par cluster")),
            value @ 1..=0x80 => u64::from(value),
            value => 1u64 << (256 - u32::from(value)).min(31),
        };
        if !sectors_per_cluster.is_power_of_two() {
            return Err(RawError::InvalidBoot("secteurs par cluster"));
        }
        let cluster_size = bytes_per_sector * sectors_per_cluster;
        if cluster_size > 2 * 1024 * 1024 {
            return Err(RawError::InvalidBoot("taille de cluster"));
        }
        let mft_lcn = u64_at(sector, 0x30);
        let record_size = match sector[0x40] as i8 {
            value @ 1..=i8::MAX => u64::from(value as u8) * cluster_size,
            value @ -31..=0 => 1u64 << (-i32::from(value)),
            _ => return Err(RawError::InvalidBoot("taille d'enregistrement")),
        };
        // Un enregistrement peut être plus petit qu'un secteur (1 Kio sur disque 4K natif) ;
        // les corrections de secteur gardent un pas de 512 octets.
        if !record_size.is_power_of_two() || !(512..=65_536).contains(&record_size) {
            return Err(RawError::InvalidBoot("taille d'enregistrement"));
        }
        Ok(Self { bytes_per_sector, cluster_size, mft_lcn, record_size: record_size as usize })
    }
}

/// Applique les corrections de secteur ; détecte un enregistrement déchiré.
pub fn apply_fixups(record: &mut [u8]) -> Result<(), RawError> {
    if record.len() < 0x30 {
        return Err(RawError::BadMftRecord("enregistrement trop court"));
    }
    let offset = usize::from(u16_at(record, 4));
    let count = usize::from(u16_at(record, 6));
    if count < 2 || record.len() % (count - 1) != 0 {
        return Err(RawError::BadMftRecord("tableau de corrections"));
    }
    let stride = record.len() / (count - 1);
    if stride < 256 || offset + 2 * count > stride - 2 {
        return Err(RawError::BadMftRecord("tableau de corrections"));
    }
    let usn = [record[offset], record[offset + 1]];
    for sector in 1..count {
        let end = sector * stride - 2;
        if record[end..end + 2] != usn {
            return Err(RawError::BadMftRecord("correction de secteur"));
        }
        let fix = offset + 2 * sector;
        record[end] = record[fix];
        record[end + 1] = record[fix + 1];
    }
    Ok(())
}

fn u16_at(bytes: &[u8], at: usize) -> u16 {
    u16::from_le_bytes([bytes[at], bytes[at + 1]])
}

fn u32_at(bytes: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(bytes[at..at + 4].try_into().expect("longueur contrôlée"))
}

fn u64_at(bytes: &[u8], at: usize) -> u64 {
    u64::from_le_bytes(bytes[at..at + 8].try_into().expect("longueur contrôlée"))
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Extent {
    /// `None` : plage creuse (sparse).
    pub lcn: Option<u64>,
    pub clusters: u64,
}

pub fn parse_data_runs(bytes: &[u8]) -> Result<Vec<Extent>, RawError> {
    let mut extents = Vec::new();
    let mut position = 0;
    let mut lcn: i64 = 0;
    loop {
        // Fin de liste : octet nul, ou fin du buffer (liste stockée sans son terminateur).
        let header = bytes.get(position).copied().unwrap_or(0);
        if header == 0 {
            return Ok(extents);
        }
        let (length_size, offset_size) = (usize::from(header & 0x0F), usize::from(header >> 4));
        if length_size == 0 || length_size > 8 || offset_size > 8 {
            return Err(RawError::BadRuns);
        }
        let fields = bytes.get(position + 1..position + 1 + length_size + offset_size).ok_or(RawError::BadRuns)?;
        let clusters = fields[..length_size].iter().rev().fold(0u64, |value, byte| (value << 8) | u64::from(*byte));
        let lcn_of_run = if offset_size == 0 {
            None
        } else {
            let raw = &fields[length_size..];
            let negative = raw[offset_size - 1] & 0x80 != 0;
            let mut delta = if negative { -1i64 } else { 0 };
            for byte in raw.iter().rev() {
                delta = (delta << 8) | i64::from(*byte);
            }
            lcn = lcn.checked_add(delta).filter(|value| *value >= 0).ok_or(RawError::BadRuns)?;
            Some(lcn as u64)
        };
        if clusters == 0 {
            return Err(RawError::BadRuns);
        }
        extents.push(Extent { lcn: lcn_of_run, clusters });
        position += 1 + length_size + offset_size;
    }
}

/// Longueur de la liste de clusters, terminateur exclu.
fn run_list_length(bytes: &[u8]) -> Option<usize> {
    let mut position = 0;
    while *bytes.get(position)? != 0 {
        let header = bytes[position];
        position += 1 + usize::from(header & 0x0F) + usize::from(header >> 4);
    }
    Some(position)
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileNameAttr {
    pub parent: u64,
    pub name: String,
    /// 0 POSIX, 1 Win32, 2 DOS (8.3 seul), 3 Win32 et DOS.
    pub namespace: u8,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct DataSize {
    pub logical: u64,
    pub allocated: u64,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ParsedRecord {
    pub record_number: u64,
    pub sequence: u16,
    /// Numéro de l'enregistrement de base (0 pour un enregistrement de base).
    pub base_record: u64,
    pub is_directory: bool,
    pub attributes: Option<u32>,
    /// FILETIME de dernière modification ($STANDARD_INFORMATION).
    pub modified: Option<u64>,
    pub names: Vec<FileNameAttr>,
    pub data: Option<DataSize>,
    pub reparse_tag: Option<u32>,
    /// Taille allouée du flux `WofCompressedData` (fichiers compressés par Windows).
    pub wof_allocated: Option<u64>,
    /// Listes de clusters du `$DATA` sans nom : (VCN de départ, octets bruts).
    pub data_runs: Vec<(u64, Vec<u8>)>,
    pub has_attribute_list: bool,
}

#[derive(Debug, PartialEq, Eq)]
pub enum RecordState {
    Unused,
    Corrupt,
    Parsed(ParsedRecord),
}

/// Décode un enregistrement FILE (les corrections de secteur sont appliquées sur place).
pub fn parse_record(record: &mut [u8], record_number: u64, keep_runs: bool) -> RecordState {
    parse_record_with(record, record_number, keep_runs, true)
}

/// Comme [`parse_record`], pour un enregistrement dont les corrections de secteur ont déjà été
/// appliquées par Windows (`FSCTL_GET_NTFS_FILE_RECORD`).
pub fn parse_prepared_record(record: &mut [u8], record_number: u64) -> RecordState {
    parse_record_with(record, record_number, false, false)
}

fn parse_record_with(record: &mut [u8], record_number: u64, keep_runs: bool, fixups: bool) -> RecordState {
    if record.len() < 0x38 {
        return RecordState::Corrupt;
    }
    match &record[0..4] {
        b"FILE" => {}
        b"BAAD" => return RecordState::Corrupt,
        _ => return RecordState::Unused,
    }
    let flags = u16_at(record, 0x16);
    if flags & 0x0001 == 0 {
        return RecordState::Unused;
    }
    if fixups && apply_fixups(record).is_err() {
        return RecordState::Corrupt;
    }
    match decode_attributes(record, record_number, flags, keep_runs) {
        Some(parsed) => RecordState::Parsed(parsed),
        None => RecordState::Corrupt,
    }
}

const ATTRIBUTE_STANDARD_INFORMATION: u32 = 0x10;
const ATTRIBUTE_LIST: u32 = 0x20;
const ATTRIBUTE_FILE_NAME: u32 = 0x30;
const ATTRIBUTE_DATA: u32 = 0x80;
const ATTRIBUTE_REPARSE_POINT: u32 = 0xC0;
const ATTRIBUTE_END: u32 = 0xFFFF_FFFF;
const WOF_STREAM_NAME: &str = "WofCompressedData";
const REPARSE_TAG_WOF: u32 = 0x8000_0017;

fn attribute_name_is(attribute: &[u8], offset: usize, length: usize, expected: &str) -> bool {
    length == expected.len()
        && attribute
            .get(offset..offset + 2 * length)
            .is_some_and(|bytes| bytes.chunks_exact(2).map(|pair| u16::from_le_bytes([pair[0], pair[1]])).eq(expected.encode_utf16()))
}

/// `None` dès qu'une longueur ou une position sort de l'enregistrement.
fn decode_attributes(record: &[u8], record_number: u64, flags: u16, keep_runs: bool) -> Option<ParsedRecord> {
    let used = u32_at(record, 0x18) as usize;
    let first = usize::from(u16_at(record, 0x14));
    if used > record.len() || used < 0x38 || first < 0x30 || first >= used {
        return None;
    }
    let mut parsed = ParsedRecord {
        record_number,
        sequence: u16_at(record, 0x10),
        base_record: u64_at(record, 0x20) & RECORD_NUMBER_MASK,
        is_directory: flags & 0x0002 != 0,
        ..ParsedRecord::default()
    };
    let mut offset = first;
    loop {
        if offset + 4 > used {
            return None;
        }
        let kind = u32_at(record, offset);
        if kind == ATTRIBUTE_END {
            return Some(parsed);
        }
        if offset + 0x18 > used {
            return None;
        }
        let length = u32_at(record, offset + 4) as usize;
        if length < 0x18 || length % 8 != 0 || offset + length > used {
            return None;
        }
        let attribute = &record[offset..offset + length];
        let non_resident = attribute[8] != 0;
        let name_length = usize::from(attribute[9]);
        let name_offset = usize::from(u16_at(attribute, 10));
        let attribute_flags = u16_at(attribute, 12);
        if name_length > 0 && name_offset + 2 * name_length > length {
            return None;
        }
        let is_wof_stream = kind == ATTRIBUTE_DATA && attribute_name_is(attribute, name_offset, name_length, WOF_STREAM_NAME);
        if is_wof_stream && !non_resident {
            parsed.wof_allocated = Some(parsed.wof_allocated.unwrap_or(0));
        }
        if non_resident {
            if length < 0x40 {
                return None;
            }
            if is_wof_stream && u64_at(attribute, 0x10) == 0 {
                parsed.wof_allocated = Some(u64_at(attribute, 0x28));
            }
            if kind == ATTRIBUTE_DATA && name_length == 0 {
                let start_vcn = u64_at(attribute, 0x10);
                if start_vcn == 0 {
                    let sparse_or_compressed = attribute_flags & 0x8001 != 0;
                    let allocated = if sparse_or_compressed && length >= 0x48 { u64_at(attribute, 0x40) } else { u64_at(attribute, 0x28) };
                    parsed.data = Some(DataSize { logical: u64_at(attribute, 0x30), allocated });
                }
                if keep_runs {
                    let runs = attribute.get(usize::from(u16_at(attribute, 0x20))..)?;
                    parsed.data_runs.push((start_vcn, runs[..run_list_length(runs)?].to_vec()));
                }
            }
        } else {
            let content_length = u32_at(attribute, 16) as usize;
            let content_offset = usize::from(u16_at(attribute, 20));
            let content = attribute.get(content_offset..content_offset.checked_add(content_length)?)?;
            match kind {
                ATTRIBUTE_STANDARD_INFORMATION if content.len() >= 0x24 => {
                    parsed.modified = Some(u64_at(content, 8));
                    parsed.attributes = Some(u32_at(content, 0x20));
                }
                ATTRIBUTE_FILE_NAME if content.len() >= 0x42 => {
                    let characters = usize::from(content[0x40]);
                    let units: Vec<u16> = content.get(0x42..0x42 + 2 * characters)?.chunks_exact(2).map(|pair| u16::from_le_bytes([pair[0], pair[1]])).collect();
                    parsed.names.push(FileNameAttr { parent: u64_at(content, 0), name: String::from_utf16_lossy(&units), namespace: content[0x41] });
                }
                ATTRIBUTE_FILE_NAME => return None,
                ATTRIBUTE_DATA if name_length == 0 => parsed.data = Some(DataSize { logical: content_length as u64, allocated: 0 }),
                ATTRIBUTE_REPARSE_POINT if content.len() >= 4 => parsed.reparse_tag = Some(u32_at(content, 0)),
                _ => {}
            }
        }
        if kind == ATTRIBUTE_LIST {
            parsed.has_attribute_list = true;
        }
        offset += length;
    }
}

#[derive(Debug, Default)]
struct Node {
    sequence: Option<u16>,
    is_directory: bool,
    attributes: u32,
    modified: u64,
    names: Vec<FileNameAttr>,
    data: Option<DataSize>,
    reparse_tag: Option<u32>,
    wof_allocated: Option<u64>,
}

#[derive(Debug, Default)]
pub struct RawStats {
    pub records_read: u64,
    pub in_use: u64,
    pub corrupt: u64,
    /// Enregistrements d'extension dont la base est absente ou libre.
    pub orphan_extensions: u64,
    /// Métafichiers NTFS et leur sous-arbre ($Extend…), exclus comme le scan classique.
    pub system_excluded: u64,
    /// Durée de la fusion finale (construction des enregistrements et des tailles), en ms.
    pub finish_ms: f64,
}

#[derive(Debug, Default)]
pub struct RawOutput {
    /// Un enregistrement par nom (lien physique), au format de l'énumération FSCTL.
    pub records: Vec<UsnRecord>,
    pub sizes: HashMap<u128, FileSizes>,
    pub reparse_tags: HashMap<u128, u32>,
    pub modified: HashMap<u128, u64>,
    pub root_id: u128,
    pub stats: RawStats,
}

/// Numéro d'enregistrement maximal accepté comme base d'une extension (garde-fou contre
/// une référence corrompue qui ferait grossir le tableau des nœuds).
const MAX_RECORD_NUMBER: u64 = 1 << 32;

/// Accumule les enregistrements décodés, extensions comprises (l'ordre d'arrivée est libre).
/// Les numéros d'enregistrement étant contigus, les nœuds sont rangés dans un tableau.
#[derive(Default)]
pub struct Collector {
    nodes: Vec<Option<Node>>,
    stats: RawStats,
}

impl Collector {
    pub fn with_capacity(records: usize) -> Self {
        let mut nodes = Vec::new();
        nodes.resize_with(records, || None);
        Self { nodes, stats: RawStats::default() }
    }

    pub fn feed(&mut self, state: RecordState) {
        self.stats.records_read += 1;
        let parsed = match state {
            RecordState::Unused => return,
            RecordState::Corrupt => {
                self.stats.corrupt += 1;
                return;
            }
            RecordState::Parsed(parsed) => parsed,
        };
        self.stats.in_use += 1;
        let target = if parsed.base_record != 0 { parsed.base_record } else { parsed.record_number };
        if target >= MAX_RECORD_NUMBER {
            self.stats.orphan_extensions += 1;
            return;
        }
        let index = target as usize;
        if index >= self.nodes.len() {
            self.nodes.resize_with(index + 1, || None);
        }
        let node = self.nodes[index].get_or_insert_with(Node::default);
        if parsed.base_record == 0 {
            node.sequence = Some(parsed.sequence);
            node.is_directory = parsed.is_directory;
            if let Some(attributes) = parsed.attributes {
                node.attributes = attributes;
            }
            if let Some(modified) = parsed.modified {
                node.modified = modified;
            }
        }
        node.names.extend(parsed.names);
        if node.data.is_none() {
            node.data = parsed.data;
        }
        if parsed.reparse_tag.is_some() {
            node.reparse_tag = parsed.reparse_tag;
        }
        if parsed.wof_allocated.is_some() {
            node.wof_allocated = parsed.wof_allocated;
        }
    }

    pub fn finish(mut self) -> RawOutput {
        let root_sequence = self.nodes.get(ROOT_RECORD as usize).and_then(|node| node.as_ref()).and_then(|node| node.sequence).unwrap_or(0);
        let system = self.system_records();
        let live = self.nodes.iter().filter(|node| node.is_some()).count();
        let mut output = RawOutput {
            root_id: (u128::from(root_sequence) << 48) | u128::from(ROOT_RECORD),
            records: Vec::with_capacity(live + live / 8),
            sizes: HashMap::with_capacity_and_hasher(live, Default::default()),
            modified: HashMap::with_capacity_and_hasher(live, Default::default()),
            ..RawOutput::default()
        };
        for (number, node) in std::mem::take(&mut self.nodes).into_iter().enumerate() {
            let Some(node) = node else { continue };
            let Some(sequence) = node.sequence else {
                self.stats.orphan_extensions += 1;
                continue;
            };
            if system[number] {
                self.stats.system_excluded += 1;
                continue;
            }
            let file_id = (u128::from(sequence) << 48) | number as u128;
            let mut attributes = node.attributes;
            if node.is_directory {
                attributes |= FILE_ATTRIBUTE_DIRECTORY;
            }
            if node.reparse_tag.is_some() {
                attributes |= FILE_ATTRIBUTE_REPARSE_POINT;
            }
            // Noms DOS 8.3 exclus, sauf s'ils sont les seuls.
            let has_long_name = node.names.iter().any(|name| name.namespace != 2);
            let mut links = 0u32;
            for name in node.names {
                if has_long_name && name.namespace == 2 {
                    continue;
                }
                links += 1;
                output.records.push(UsnRecord { file_id, parent_id: u128::from(name.parent), attributes, name: name.name, major_version: 0 });
            }
            if !node.is_directory {
                let data = node.data.unwrap_or_default();
                // Fichier compressé par Windows : les données occupent le flux WofCompressedData.
                let compressed = if node.reparse_tag == Some(REPARSE_TAG_WOF) { node.wof_allocated.unwrap_or(0) } else { 0 };
                let allocated = data.allocated.saturating_add(compressed);
                output.sizes.insert(file_id, FileSizes { logical: data.logical, allocated, links });
            }
            if let Some(tag) = node.reparse_tag {
                output.reparse_tags.insert(file_id, tag);
            }
            if node.modified != 0 {
                output.modified.insert(file_id, node.modified);
            }
        }
        output.stats = self.stats;
        output
    }

    /// Métafichiers (enregistrements < 16 hors racine) et tout ce qui descend d'eux.
    fn system_records(&self) -> Vec<bool> {
        const UNKNOWN: u8 = 0;
        const SYSTEM: u8 = 1;
        const USER: u8 = 2;
        let count = self.nodes.len();
        let mut verdicts = vec![UNKNOWN; count];
        let parent_of = |number: usize| -> Option<usize> {
            let parent = self.nodes.get(number)?.as_ref()?.names.first()?.parent & RECORD_NUMBER_MASK;
            usize::try_from(parent).ok()
        };
        let mut chain = Vec::new();
        for start in 0..count {
            if self.nodes[start].is_none() || verdicts[start] != UNKNOWN {
                continue;
            }
            chain.clear();
            let mut current = start;
            let verdict = loop {
                if current < count && verdicts[current] != UNKNOWN {
                    break verdicts[current];
                }
                if current as u64 == ROOT_RECORD {
                    break USER;
                }
                if (current as u64) < FIRST_USER_RECORD {
                    break SYSTEM;
                }
                if chain.len() > count || current >= count {
                    break USER;
                }
                chain.push(current);
                match parent_of(current) {
                    Some(parent) if parent != current => current = parent,
                    _ => break USER,
                }
            };
            verdicts[start] = verdict;
            for &number in &chain {
                verdicts[number] = verdict;
            }
        }
        verdicts.into_iter().enumerate().map(|(number, verdict)| verdict == SYSTEM && number as u64 != ROOT_RECORD).collect()
    }
}


/// Taille des lectures de la `$MFT` (multiple de toute taille de cluster/enregistrement usuelle).
const CHUNK_BYTES: u64 = 4 * 1024 * 1024;

/// Alignement mémoire des tampons de lecture du volume : les disques à secteurs de 4 Ko
/// refusent une mémoire non alignée (erreur Windows 50, constatée sur un disque dur 4K natif).
const BUFFER_ALIGNMENT: usize = 4096;

/// Tampon dont le début est aligné sur `BUFFER_ALIGNMENT` (sans code non sûr).
struct AlignedBuffer {
    data: Vec<u8>,
    start: usize,
    len: usize,
}

impl AlignedBuffer {
    fn new(len: usize) -> Self {
        let data = vec![0u8; len + BUFFER_ALIGNMENT];
        let start = data.as_ptr().align_offset(BUFFER_ALIGNMENT).min(BUFFER_ALIGNMENT);
        Self { data, start, len }
    }

    fn as_mut_slice(&mut self) -> &mut [u8] {
        &mut self.data[self.start..self.start + self.len]
    }
}

/// Lit et décode toute la `$MFT`. `read_at(offset, buffer)` remplit entièrement le buffer
/// (offsets et longueurs multiples de la taille de secteur). `keep_going(lus, total)`
/// permet la progression et l'annulation.
pub fn read_mft(
    read_at: impl Fn(u64, &mut [u8]) -> Result<(), String> + Sync,
    keep_going: impl FnMut(u64, u64) -> bool,
) -> Result<RawOutput, RawError> {
    read_mft_with(CHUNK_BYTES, read_at, keep_going)
}

/// Comme [`read_mft`], avec une taille de lecture choisie (tests).
pub fn read_mft_with(
    chunk_bytes: u64,
    read_at: impl Fn(u64, &mut [u8]) -> Result<(), String> + Sync,
    keep_going: impl FnMut(u64, u64) -> bool,
) -> Result<RawOutput, RawError> {
    let mut boot = AlignedBuffer::new(4096);
    read_at(0, boot.as_mut_slice()).map_err(RawError::Io)?;
    let geometry = BootSector::parse(boot.as_mut_slice())?;
    let record_size = geometry.record_size as u64;
    let round_up = |bytes: u64| bytes.div_ceil(geometry.bytes_per_sector) * geometry.bytes_per_sector;

    let mut first = AlignedBuffer::new(round_up(record_size) as usize);
    read_at(geometry.mft_lcn * geometry.cluster_size, first.as_mut_slice()).map_err(RawError::Io)?;
    let RecordState::Parsed(mft) = parse_record(&mut first.as_mut_slice()[..geometry.record_size], 0, true) else {
        return Err(RawError::BadMftRecord("enregistrement 0 ($MFT)"));
    };
    let mft_bytes = mft.data.ok_or(RawError::BadMftRecord("$DATA de la $MFT absent"))?.logical;
    let mut runs = mft.data_runs;
    runs.sort_by_key(|(start_vcn, _)| *start_vcn);
    let mut extents = Vec::new();
    for (_, bytes) in &runs {
        extents.extend(parse_data_runs(bytes)?);
    }
    let covered: u64 = extents.iter().map(|extent| extent.clusters * geometry.cluster_size).sum();
    if covered < mft_bytes {
        return Err(RawError::Unsupported("$MFT décrite par une liste d'attributs"));
    }
    if extents.iter().any(|extent| extent.lcn.is_none()) {
        return Err(RawError::Unsupported("$MFT creuse"));
    }

    // Plan de lecture : (offset disque, octets, premier enregistrement, nombre d'enregistrements).
    let total_records = mft_bytes / record_size;
    let chunk_capacity = (chunk_bytes / geometry.cluster_size).max(1) * geometry.cluster_size;
    let mut plan = Vec::new();
    let mut first_record = 0u64;
    'extents: for extent in extents {
        let start = extent.lcn.expect("plages creuses refusées") * geometry.cluster_size;
        let extent_bytes = extent.clusters * geometry.cluster_size;
        let mut done = 0u64;
        while done < extent_bytes {
            let remaining_records = total_records - first_record;
            if remaining_records == 0 {
                break 'extents;
            }
            let wanted = (extent_bytes - done).min(chunk_capacity).min(round_up(remaining_records * record_size));
            let records = (wanted / record_size).min(remaining_records);
            plan.push((start + done, wanted, first_record, records));
            first_record += records;
            done += wanted;
        }
    }
    decode_in_parallel(&plan, geometry.record_size, total_records, read_at, keep_going)
}

/// Géométrie fournie par `FSCTL_GET_NTFS_VOLUME_DATA`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MftGeometry {
    pub record_size: usize,
    pub cluster_size: u64,
    /// `MftValidDataLength` : octets utiles du fichier `$MFT`.
    pub valid_bytes: u64,
}

/// Lit la `$MFT` enregistrement par enregistrement avec `FSCTL_GET_NTFS_FILE_RECORD`, quand
/// Windows refuse la lecture brute du volume (constaté sur un disque dur : erreur 50 dès le
/// secteur de démarrage ; l'ouverture de `X:\$MFT` est elle aussi refusée).
///
/// `fetch(n, tampon)` renvoie l'enregistrement `n` ou, s'il est libre, l'enregistrement occupé
/// le plus proche en dessous : `(numéro trouvé, longueur)`. Le parcours descendant ne fait donc
/// qu'un appel par enregistrement occupé. Le décodage est celui de [`read_mft`].
pub fn read_mft_by_records(
    total_records: u64,
    mut fetch: impl FnMut(u64, &mut [u8]) -> Result<(u64, usize), String>,
    mut keep_going: impl FnMut(u64, u64) -> bool,
) -> Result<RawOutput, RawError> {
    const PROGRESS_STEP: u64 = 4096;
    let mut collector = Collector::with_capacity(total_records as usize);
    let mut buffer = vec![0u8; 65_536];
    let mut next = total_records.checked_sub(1);
    let (mut done, mut reported) = (0u64, 0u64);
    while let Some(requested) = next {
        let (found, length) = fetch(requested, &mut buffer).map_err(RawError::Io)?;
        if found > requested || length > buffer.len() {
            return Err(RawError::BadMftRecord("réponse FSCTL_GET_NTFS_FILE_RECORD inattendue"));
        }
        // Les enregistrements found+1..=requested sont libres.
        collector.stats.records_read += requested - found;
        collector.feed(parse_prepared_record(&mut buffer[..length], found));
        done += requested - found + 1;
        if done - reported >= PROGRESS_STEP {
            reported = done;
            if !keep_going(done, total_records) {
                return Err(RawError::Cancelled);
            }
        }
        next = found.checked_sub(1);
    }
    let started = std::time::Instant::now();
    let mut output = collector.finish();
    output.stats.finish_ms = started.elapsed().as_secs_f64() * 1000.0;
    Ok(output)
}

/// Un fil lit les morceaux **dans l'ordre du disque** (lecture séquentielle : efficace sur
/// disque dur comme sur SSD ; des lectures simultanées font sauter la tête d'un disque
/// mécanique), plusieurs fils les décodent, le fil appelant fusionne les résultats (la fusion
/// ne dépend pas de l'ordre d'arrivée) et décide de l'annulation.
fn decode_in_parallel(
    plan: &[(u64, u64, u64, u64)],
    record_size: usize,
    total_records: u64,
    read_at: impl Fn(u64, &mut [u8]) -> Result<(), String> + Sync,
    mut keep_going: impl FnMut(u64, u64) -> bool,
) -> Result<RawOutput, RawError> {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::{mpsc, Arc, Mutex};

    let workers = std::thread::available_parallelism().map_or(4, |count| count.get()).clamp(1, 8).min(plan.len().max(1));
    let stop = AtomicBool::new(false);
    let mut collector = Collector::with_capacity(total_records as usize);
    std::thread::scope(|scope| {
        let (result_sender, result_receiver) = mpsc::channel::<Result<(u64, Vec<RecordState>), RawError>>();
        let (job_sender, job_receiver) = mpsc::sync_channel::<(u64, u64, AlignedBuffer)>(workers * 2);
        let job_receiver = Arc::new(Mutex::new(job_receiver));
        for _ in 0..workers {
            let jobs = Arc::clone(&job_receiver);
            let results = result_sender.clone();
            scope.spawn(move || loop {
                let job = jobs.lock().map(|receiver| receiver.recv());
                let Ok(Ok((first, count, mut buffer))) = job else { break };
                let states: Vec<RecordState> = buffer
                    .as_mut_slice()
                    .chunks_exact_mut(record_size)
                    .take(count as usize)
                    .enumerate()
                    .map(|(position, record)| parse_record(record, first + position as u64, false))
                    .filter(|state| !matches!(state, RecordState::Unused))
                    .collect();
                if results.send(Ok((count, states))).is_err() {
                    break;
                }
            });
        }
        // Seuls les fils de décodage détiennent le récepteur : s'ils s'arrêtent, l'envoi du
        // lecteur échoue au lieu de bloquer sur un canal plein.
        drop(job_receiver);

        let reader_results = result_sender.clone();
        let (stop_flag, read_at) = (&stop, &read_at);
        scope.spawn(move || {
            for &(offset, bytes, first, count) in plan {
                if stop_flag.load(Ordering::Relaxed) {
                    break;
                }
                let mut buffer = AlignedBuffer::new(bytes as usize);
                if let Err(error) = read_at(offset, buffer.as_mut_slice()) {
                    let _ = reader_results.send(Err(RawError::Io(error)));
                    break;
                }
                if job_sender.send((first, count, buffer)).is_err() {
                    break;
                }
            }
        });
        drop(result_sender);

        let mut done = 0u64;
        let mut outcome = Ok(());
        while done < total_records {
            let Ok(message) = result_receiver.recv() else { break };
            match message {
                Ok((count, states)) => {
                    collector.stats.records_read += count - states.len() as u64;
                    for state in states {
                        collector.feed(state);
                    }
                    done += count;
                    if !keep_going(done, total_records) {
                        outcome = Err(RawError::Cancelled);
                        break;
                    }
                }
                Err(error) => {
                    outcome = Err(error);
                    break;
                }
            }
        }
        // Les fils s'arrêtent au prochain morceau ; le canal non borné ne les bloque jamais.
        stop.store(true, Ordering::Relaxed);
        outcome
    })?;
    let started = std::time::Instant::now();
    let mut output = collector.finish();
    output.stats.finish_ms = started.elapsed().as_secs_f64() * 1000.0;
    Ok(output)
}

#[cfg(test)]
pub(crate) mod test_support {
    //! Construction d'enregistrements et de volumes NTFS synthétiques.

    pub const RECORD: usize = 1024;
    pub const SECTOR: usize = 512;
    pub const CLUSTER: usize = 4096;

    pub fn put16(buffer: &mut [u8], at: usize, value: u16) {
        buffer[at..at + 2].copy_from_slice(&value.to_le_bytes());
    }
    pub fn put32(buffer: &mut [u8], at: usize, value: u32) {
        buffer[at..at + 4].copy_from_slice(&value.to_le_bytes());
    }
    pub fn put64(buffer: &mut [u8], at: usize, value: u64) {
        buffer[at..at + 8].copy_from_slice(&value.to_le_bytes());
    }

    pub fn boot_sector(mft_lcn: u64) -> Vec<u8> {
        let mut sector = vec![0u8; SECTOR];
        sector[3..11].copy_from_slice(b"NTFS    ");
        put16(&mut sector, 0x0B, SECTOR as u16);
        sector[0x0D] = (CLUSTER / SECTOR) as u8;
        put64(&mut sector, 0x30, mft_lcn);
        sector[0x40] = (-10i8) as u8; // 2^10 = 1024 octets par enregistrement
        put16(&mut sector, 0x1FE, 0xAA55);
        sector
    }

    fn pad8(mut bytes: Vec<u8>) -> Vec<u8> {
        while bytes.len() % 8 != 0 {
            bytes.push(0);
        }
        bytes
    }

    pub fn resident(kind: u32, name: &str, content: &[u8]) -> Vec<u8> {
        let name_units: Vec<u8> = name.encode_utf16().flat_map(u16::to_le_bytes).collect();
        let content_offset = (0x18 + name_units.len() + 7) / 8 * 8;
        let mut attribute = vec![0u8; content_offset];
        put32(&mut attribute, 0, kind);
        attribute[8] = 0;
        attribute[9] = (name_units.len() / 2) as u8;
        put16(&mut attribute, 10, 0x18);
        attribute[0x18..0x18 + name_units.len()].copy_from_slice(&name_units);
        put32(&mut attribute, 16, content.len() as u32);
        put16(&mut attribute, 20, content_offset as u16);
        attribute.extend_from_slice(content);
        let mut attribute = pad8(attribute);
        let length = attribute.len() as u32;
        put32(&mut attribute, 4, length);
        attribute
    }

    pub struct NonResident {
        pub start_vcn: u64,
        pub last_vcn: u64,
        pub allocated: u64,
        pub real: u64,
        pub compressed: Option<u64>,
        pub runs: Vec<u8>,
        pub name: &'static str,
    }

    pub fn non_resident(kind: u32, spec: &NonResident) -> Vec<u8> {
        let name_units: Vec<u8> = spec.name.encode_utf16().flat_map(u16::to_le_bytes).collect();
        let header = if spec.compressed.is_some() { 0x48 } else { 0x40 };
        let runs_offset = (header + name_units.len() + 7) / 8 * 8;
        let mut attribute = vec![0u8; runs_offset];
        put32(&mut attribute, 0, kind);
        attribute[8] = 1;
        attribute[9] = (name_units.len() / 2) as u8;
        put16(&mut attribute, 10, header as u16);
        attribute[header..header + name_units.len()].copy_from_slice(&name_units);
        if spec.compressed.is_some() {
            put16(&mut attribute, 12, 0x8000); // sparse
        }
        put64(&mut attribute, 0x10, spec.start_vcn);
        put64(&mut attribute, 0x18, spec.last_vcn);
        put16(&mut attribute, 0x20, runs_offset as u16);
        put64(&mut attribute, 0x28, spec.allocated);
        put64(&mut attribute, 0x30, spec.real);
        put64(&mut attribute, 0x38, spec.real);
        if let Some(compressed) = spec.compressed {
            put64(&mut attribute, 0x40, compressed);
        }
        attribute.extend_from_slice(&spec.runs);
        attribute.push(0);
        let mut attribute = pad8(attribute);
        let length = attribute.len() as u32;
        put32(&mut attribute, 4, length);
        attribute
    }

    pub fn standard_information(attributes: u32, modified: u64) -> Vec<u8> {
        let mut content = vec![0u8; 0x48];
        put64(&mut content, 8, modified);
        put32(&mut content, 0x20, attributes);
        resident(0x10, "", &content)
    }

    pub fn file_name(parent: u64, name: &str, namespace: u8) -> Vec<u8> {
        let units: Vec<u8> = name.encode_utf16().flat_map(u16::to_le_bytes).collect();
        let mut content = vec![0u8; 0x42];
        put64(&mut content, 0, parent);
        content[0x40] = (units.len() / 2) as u8;
        content[0x41] = namespace;
        content.extend_from_slice(&units);
        resident(0x30, "", &content)
    }

    pub fn reparse_point(tag: u32) -> Vec<u8> {
        let mut content = vec![0u8; 8];
        put32(&mut content, 0, tag);
        resident(0xC0, "", &content)
    }

    pub struct Record {
        pub sequence: u16,
        pub flags: u16,
        pub base: u64,
        pub attributes: Vec<Vec<u8>>,
    }

    /// Enregistrement complet avec corrections de secteur (USN = 0x0042).
    pub fn record(spec: &Record) -> Vec<u8> {
        let mut record = vec![0u8; RECORD];
        record[0..4].copy_from_slice(b"FILE");
        put16(&mut record, 4, 0x30);
        put16(&mut record, 6, (RECORD / SECTOR + 1) as u16);
        put16(&mut record, 0x10, spec.sequence);
        put16(&mut record, 0x14, 0x38);
        put16(&mut record, 0x16, spec.flags);
        put64(&mut record, 0x20, spec.base);
        let mut offset = 0x38;
        for attribute in &spec.attributes {
            record[offset..offset + attribute.len()].copy_from_slice(attribute);
            offset += attribute.len();
        }
        put32(&mut record, offset, 0xFFFF_FFFF);
        offset += 8;
        put32(&mut record, 0x18, offset as u32);
        put32(&mut record, 0x1C, RECORD as u32);
        let usn = 0x0042u16;
        put16(&mut record, 0x30, usn);
        for sector in 1..=RECORD / SECTOR {
            let end = sector * SECTOR - 2;
            let original = [record[end], record[end + 1]];
            record[0x30 + 2 * sector..0x30 + 2 * sector + 2].copy_from_slice(&original);
            put16(&mut record, end, usn);
        }
        record
    }

    pub const IN_USE: u16 = 0x0001;
    pub const DIRECTORY: u16 = 0x0002;
}

#[cfg(test)]
mod tests {
    use super::test_support::*;
    use super::*;

    fn simple_file(sequence: u16, parent: u64, name: &str, real: u64) -> Vec<u8> {
        record(&Record {
            sequence,
            flags: IN_USE,
            base: 0,
            attributes: vec![
                standard_information(0x20, 777),
                file_name(parent, name, 1),
                non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 0, allocated: 4096, real, compressed: None, runs: vec![0x11, 0x01, 0x20], name: "" }),
            ],
        })
    }

    #[test]
    fn boot_sector_gives_geometry() {
        let boot = BootSector::parse(&boot_sector(4)).unwrap();
        assert_eq!(boot, BootSector { bytes_per_sector: 512, cluster_size: 4096, mft_lcn: 4, record_size: 1024 });
    }

    #[test]
    fn boot_sector_accepts_1k_records_on_4k_native_sectors() {
        // Disque « 4K natif » (cas réel E: du 13/11/2026) : secteur 4096, cluster 4096,
        // enregistrements MFT de 1024 octets.
        let mut sector = boot_sector(4);
        put16(&mut sector, 0x0B, 4096);
        sector[0x0D] = 1;
        let boot = BootSector::parse(&sector).unwrap();
        assert_eq!((boot.bytes_per_sector, boot.cluster_size, boot.record_size), (4096, 4096, 1024));
    }

    #[test]
    fn boot_sector_rejects_non_ntfs_and_absurd_geometry() {
        let mut sector = boot_sector(4);
        sector[3..11].copy_from_slice(b"EXFAT   ");
        assert_eq!(BootSector::parse(&sector), Err(RawError::NotNtfs));
        let mut sector = boot_sector(4);
        put16(&mut sector, 0x0B, 1000);
        assert!(matches!(BootSector::parse(&sector), Err(RawError::InvalidBoot(_))));
        assert!(matches!(BootSector::parse(&[0u8; 100]), Err(RawError::InvalidBoot(_))));
    }

    #[test]
    fn fixups_restore_sector_ends_and_detect_torn_records() {
        let mut built = simple_file(1, 5, "a.txt", 10);
        let mut torn = built.clone();
        apply_fixups(&mut built).unwrap();
        assert_ne!(u16::from_le_bytes([built[510], built[511]]), 0x0042);
        put16(&mut torn, 1022, 0x9999);
        assert!(apply_fixups(&mut torn).is_err());
    }

    #[test]
    fn data_runs_decode_relative_and_sparse_extents() {
        // 0x21 : longueur 1 octet, offset 2 octets. Puis offset négatif, puis plage creuse.
        let runs = [0x21, 0x10, 0x00, 0x01, 0x21, 0x08, 0x00, 0xFF, 0x01, 0x04, 0x00];
        assert_eq!(
            parse_data_runs(&runs).unwrap(),
            [
                Extent { lcn: Some(0x100), clusters: 0x10 },
                Extent { lcn: Some(0x100 - 0x100), clusters: 8 },
                Extent { lcn: None, clusters: 4 },
            ]
        );
        assert_eq!(parse_data_runs(&[0x91, 0x01, 0x00]), Err(RawError::BadRuns));
        assert_eq!(parse_data_runs(&[0x21, 0x10]), Err(RawError::BadRuns));
    }

    #[test]
    fn parses_names_sizes_attributes_and_date() {
        let mut built = record(&Record {
            sequence: 3,
            flags: IN_USE,
            base: 0,
            attributes: vec![
                standard_information(0x21, 0x0123_4567),
                file_name(5 | (5 << 48), "LONGNO~1.TXT", 2),
                file_name(5 | (5 << 48), "Long nom.txt", 1),
                non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 2, allocated: 12288, real: 10000, compressed: None, runs: vec![0x11, 0x03, 0x20], name: "" }),
                non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 9, allocated: 999_999, real: 999_999, compressed: None, runs: vec![0x11, 0x0A, 0x40], name: "Zone.Identifier" }),
            ],
        });
        let RecordState::Parsed(parsed) = parse_record(&mut built, 40, false) else { panic!("non décodé") };
        assert_eq!((parsed.record_number, parsed.sequence, parsed.base_record, parsed.is_directory), (40, 3, 0, false));
        assert_eq!((parsed.attributes, parsed.modified), (Some(0x21), Some(0x0123_4567)));
        assert_eq!(parsed.names.iter().map(|n| (n.name.as_str(), n.namespace)).collect::<Vec<_>>(), [("LONGNO~1.TXT", 2), ("Long nom.txt", 1)]);
        assert_eq!(parsed.names[1].parent, 5 | (5 << 48));
        assert_eq!(parsed.data, Some(DataSize { logical: 10000, allocated: 12288 }));
        assert!(parsed.data_runs.is_empty());
    }

    #[test]
    fn resident_data_has_no_allocation_and_sparse_uses_compressed_size() {
        let mut small = record(&Record { sequence: 1, flags: IN_USE, base: 0, attributes: vec![file_name(5, "petit", 1), resident(0x80, "", b"bonjour")] });
        let RecordState::Parsed(parsed) = parse_record(&mut small, 41, false) else { panic!() };
        assert_eq!(parsed.data, Some(DataSize { logical: 7, allocated: 0 }));

        let mut sparse = record(&Record {
            sequence: 1,
            flags: IN_USE,
            base: 0,
            attributes: vec![non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 99, allocated: 409_600, real: 409_600, compressed: Some(8192), runs: vec![0x01, 0x64], name: "" })],
        });
        let RecordState::Parsed(parsed) = parse_record(&mut sparse, 42, false) else { panic!() };
        assert_eq!(parsed.data, Some(DataSize { logical: 409_600, allocated: 8192 }));
    }

    #[test]
    fn wof_compressed_files_count_their_compressed_stream_as_allocated() {
        let wof_file = |number: u64| {
            record(&Record {
                sequence: 1,
                flags: IN_USE,
                base: 0,
                attributes: vec![
                    standard_information(0x20 | 0x400, 1),
                    file_name(5 | (5 << 48), &format!("f{number}.dll"), 1),
                    non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 87, allocated: 360_448, real: 360_448, compressed: Some(0), runs: vec![0x01, 0x58], name: "" }),
                    non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 54, allocated: 225_280, real: 224_000, compressed: None, runs: vec![0x11, 0x37, 0x20], name: "WofCompressedData" }),
                    reparse_point(0x8000_0017),
                ],
            })
        };
        let RecordState::Parsed(parsed) = parse_record(&mut wof_file(70), 70, false) else { panic!() };
        assert_eq!(parsed.wof_allocated, Some(225_280));

        let mut collector = Collector::default();
        collector.feed(parsed_state(root_record(), 5));
        collector.feed(parsed_state(wof_file(70), 70));
        let output = collector.finish();
        assert_eq!(output.sizes.get(&(70u128 | (1u128 << 48))), Some(&FileSizes { logical: 360_448, allocated: 225_280, links: 1 }));
    }

    fn parsed_state(mut bytes: Vec<u8>, number: u64) -> RecordState {
        parse_record(&mut bytes, number, false)
    }

    #[test]
    fn parses_directory_flag_reparse_tag_and_extension_base() {
        let mut built = record(&Record { sequence: 2, flags: IN_USE | DIRECTORY, base: 77 | (2 << 48), attributes: vec![reparse_point(0xA000_0003)] });
        let RecordState::Parsed(parsed) = parse_record(&mut built, 50, false) else { panic!() };
        assert!(parsed.is_directory);
        assert_eq!((parsed.base_record, parsed.reparse_tag), (77, Some(0xA000_0003)));
    }

    #[test]
    fn keeps_data_runs_only_when_requested() {
        let mut built = simple_file(1, 5, "$MFT", 8192);
        let RecordState::Parsed(parsed) = parse_record(&mut built, 0, true) else { panic!() };
        assert_eq!(parsed.data_runs, [(0, vec![0x11, 0x01, 0x20])]);
    }

    #[test]
    fn classifies_unused_baad_and_overflowing_records() {
        let mut free = simple_file(1, 5, "x", 1);
        put16(&mut free, 0x16, 0);
        assert_eq!(parse_record(&mut free, 60, false), RecordState::Unused);
        assert_eq!(parse_record(&mut vec![0u8; RECORD], 61, false), RecordState::Unused);
        let mut baad = simple_file(1, 5, "x", 1);
        baad[0..4].copy_from_slice(b"BAAD");
        assert_eq!(parse_record(&mut baad, 62, false), RecordState::Corrupt);
        let mut overflow = simple_file(1, 5, "x", 1);
        put32(&mut overflow, 0x38 + 4, 4000); // longueur d'attribut hors enregistrement
        assert_eq!(parse_record(&mut overflow, 63, false), RecordState::Corrupt);
    }

    fn parsed(mut bytes: Vec<u8>, number: u64) -> RecordState {
        parse_record(&mut bytes, number, false)
    }

    fn root_record() -> Vec<u8> {
        record(&Record { sequence: 5, flags: IN_USE | DIRECTORY, base: 0, attributes: vec![standard_information(0x16, 1), file_name(5 | (5 << 48), ".", 3)] })
    }

    #[test]
    fn collector_emits_one_record_per_name_and_merges_extensions() {
        let mut collector = Collector::default();
        // L'extension (record 41) arrive avant sa base : son second nom et ses données sont fusionnés.
        collector.feed(parsed(
            record(&Record {
                sequence: 1,
                flags: IN_USE,
                base: 40 | (7 << 48),
                attributes: vec![
                    file_name(5 | (5 << 48), "alias.bin", 1),
                    non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 0, allocated: 4096, real: 300, compressed: None, runs: vec![0x11, 0x01, 0x20], name: "" }),
                ],
            }),
            41,
        ));
        collector.feed(parsed(root_record(), 5));
        collector.feed(parsed(
            record(&Record { sequence: 7, flags: IN_USE, base: 0, attributes: vec![standard_information(0x20, 99), file_name(5 | (5 << 48), "ORIGIN~1.BIN", 2), file_name(5 | (5 << 48), "original.bin", 1)] }),
            40,
        ));
        let output = collector.finish();
        let id = 40u128 | (7u128 << 48);
        let mut names: Vec<_> = output.records.iter().filter(|r| r.file_id == id).map(|r| r.name.as_str()).collect();
        names.sort_unstable();
        assert_eq!(names, ["alias.bin", "original.bin"]);
        assert_eq!(output.sizes.get(&id), Some(&FileSizes { logical: 300, allocated: 4096, links: 2 }));
        assert_eq!(output.modified.get(&id), Some(&99));
        assert_eq!(output.root_id, 5 | (5 << 48));
        let root = output.records.iter().find(|r| r.file_id == output.root_id).unwrap();
        assert_ne!(root.attributes & FILE_ATTRIBUTE_DIRECTORY, 0);
    }

    #[test]
    fn collector_excludes_metafiles_and_their_subtree() {
        let mut collector = Collector::default();
        collector.feed(parsed(root_record(), 5));
        collector.feed(parsed(record(&Record { sequence: 1, flags: IN_USE, base: 0, attributes: vec![file_name(5 | (5 << 48), "$MFT", 3)] }), 0));
        collector.feed(parsed(record(&Record { sequence: 11, flags: IN_USE | DIRECTORY, base: 0, attributes: vec![file_name(5 | (5 << 48), "$Extend", 3)] }), 11));
        collector.feed(parsed(record(&Record { sequence: 1, flags: IN_USE | DIRECTORY, base: 0, attributes: vec![file_name(11 | (11 << 48), "$RmMetadata", 3)] }), 30));
        collector.feed(parsed(record(&Record { sequence: 1, flags: IN_USE, base: 0, attributes: vec![file_name(30 | (1 << 48), "$TxfLog", 3)] }), 31));
        collector.feed(parsed(simple_file(1, 5 | (5 << 48), "user.txt", 5), 32));
        let output = collector.finish();
        let names: Vec<_> = output.records.iter().map(|r| r.name.as_str()).filter(|n| *n != ".").collect();
        assert_eq!(names, ["user.txt"]);
        assert_eq!(output.stats.system_excluded, 4);
    }

    #[test]
    fn collector_marks_reparse_points_and_counts_orphans_and_corruption() {
        let mut collector = Collector::default();
        collector.feed(parsed(root_record(), 5));
        collector.feed(parsed(
            record(&Record { sequence: 1, flags: IN_USE, base: 0, attributes: vec![standard_information(0x20, 1), file_name(5 | (5 << 48), "lien", 1), reparse_point(0xA000_000C)] }),
            33,
        ));
        collector.feed(parsed(record(&Record { sequence: 1, flags: IN_USE, base: 900 | (1 << 48), attributes: vec![file_name(5, "x", 1)] }), 34));
        collector.feed(RecordState::Corrupt);
        let output = collector.finish();
        let link = output.records.iter().find(|r| r.name == "lien").unwrap();
        assert_ne!(link.attributes & FILE_ATTRIBUTE_REPARSE_POINT, 0);
        assert_eq!(output.reparse_tags.get(&link.file_id), Some(&0xA000_000C));
        assert_eq!((output.stats.orphan_extensions, output.stats.corrupt), (1, 1));
    }

    /// Volume synthétique : secteur de boot, puis la $MFT au cluster 2 (20 enregistrements
    /// de 1 Kio sur 5 clusters), décrite par le $DATA de l'enregistrement 0.
    /// Les enregistrements 0 à 15 sont réservés aux métafichiers NTFS.
    fn synthetic_volume(records: &[(u64, Vec<u8>)]) -> Vec<u8> {
        let mut volume = vec![0u8; CLUSTER * 8];
        volume[..SECTOR].copy_from_slice(&boot_sector(2));
        let mft = record(&Record {
            sequence: 1,
            flags: IN_USE,
            base: 0,
            attributes: vec![
                file_name(5 | (5 << 48), "$MFT", 3),
                non_resident(0x80, &NonResident { start_vcn: 0, last_vcn: 4, allocated: 20480, real: 20480, compressed: None, runs: vec![0x11, 0x05, 0x02], name: "" }),
            ],
        });
        let base = 2 * CLUSTER;
        volume[base..base + RECORD].copy_from_slice(&mft);
        for (number, bytes) in records {
            let at = base + *number as usize * RECORD;
            volume[at..at + RECORD].copy_from_slice(bytes);
        }
        volume
    }

    #[test]
    fn read_mft_follows_runs_and_decodes_every_record() {
        let volume = synthetic_volume(&[(5, root_record()), (16, simple_file(2, 5 | (5 << 48), "a.txt", 123)), (17, simple_file(1, 5 | (5 << 48), "b.txt", 456))]);
        let reads = std::sync::Mutex::new(Vec::new());
        let mut progress = Vec::new();
        let output = read_mft(
            |offset, buffer| {
                reads.lock().unwrap().push((offset, buffer.len()));
                // Les disques à secteurs de 4 Ko exigent une mémoire alignée (erreur Windows 50 sinon).
                assert_eq!(buffer.as_ptr() as usize % 4096, 0, "tampon de lecture non aligné");
                let start = offset as usize;
                buffer.copy_from_slice(volume.get(start..start + buffer.len()).ok_or("hors volume")?);
                Ok(())
            },
            |done, total| {
                progress.push((done, total));
                true
            },
        )
        .unwrap();
        let mut names: Vec<_> = output.records.iter().map(|r| r.name.as_str()).filter(|n| *n != ".").collect();
        names.sort_unstable();
        assert_eq!(names, ["a.txt", "b.txt"]);
        assert_eq!(output.sizes.get(&(16u128 | (2u128 << 48))).map(|s| s.logical), Some(123));
        assert_eq!(output.stats.records_read, 20);
        let reads = reads.into_inner().unwrap();
        assert!(reads.iter().all(|(offset, len)| offset % 512 == 0 && len % 512 == 0), "{reads:?}");
        assert_eq!(progress.last(), Some(&(20, 20)));
    }

    #[test]
    fn read_mft_decodes_identically_when_split_in_many_chunks() {
        let mut records = vec![(5, root_record())];
        for number in 16..20 {
            records.push((number, simple_file(1, 5 | (5 << 48), &format!("f{number}.bin"), number * 10)));
        }
        let volume = synthetic_volume(&records);
        let mut progress = Vec::new();
        // Un cluster (4 enregistrements) par lecture : 5 morceaux décodés en parallèle.
        let output = read_mft_with(
            CLUSTER as u64,
            |offset, buffer| {
                let start = offset as usize;
                buffer.copy_from_slice(&volume[start..start + buffer.len()]);
                Ok(())
            },
            |done, total| {
                progress.push((done, total));
                true
            },
        )
        .unwrap();
        let mut names: Vec<_> = output.records.iter().map(|r| r.name.clone()).filter(|n| n != ".").collect();
        names.sort_unstable();
        assert_eq!(names, ["f16.bin", "f17.bin", "f18.bin", "f19.bin"]);
        assert_eq!(output.stats.records_read, 20);
        assert_eq!(progress.len(), 5);
        assert_eq!(progress.last(), Some(&(20, 20)));
        assert_eq!(output.sizes.get(&(19u128 | (1u128 << 48))).map(|s| s.logical), Some(190));
    }

    #[test]
    fn reads_records_one_by_one_skipping_free_ones_downwards() {
        // FSCTL_GET_NTFS_FILE_RECORD : renvoie l'enregistrement demandé, ou l'enregistrement
        // occupé le plus proche en dessous ; les corrections de secteur sont déjà appliquées.
        let mut table: Vec<Option<Vec<u8>>> = vec![None; 40];
        for (number, mut bytes) in [(0usize, root_record()), (5, root_record()), (16, simple_file(1, 5 | (5 << 48), "a.bin", 11)), (31, simple_file(2, 5 | (5 << 48), "b.bin", 22))] {
            apply_fixups(&mut bytes).unwrap();
            table[number] = Some(bytes);
        }
        let mut calls = Vec::new();
        let output = read_mft_by_records(
            40,
            |requested, buffer| {
                calls.push(requested);
                let found = (0..=requested as usize).rev().find(|n| table[*n].is_some()).ok_or("aucun")?;
                let bytes = table[found].as_ref().unwrap();
                buffer[..bytes.len()].copy_from_slice(bytes);
                Ok((found as u64, bytes.len()))
            },
            |_, _| true,
        )
        .unwrap();
        let mut names: Vec<_> = output.records.iter().map(|r| r.name.as_str()).filter(|n| *n != ".").collect();
        names.sort_unstable();
        assert_eq!(names, ["a.bin", "b.bin"]);
        assert_eq!(calls, [39, 30, 15, 4]);
        assert_eq!(output.stats.records_read, 40);
        assert_eq!(output.stats.corrupt, 0);
    }

    #[test]
    fn read_mft_stops_on_cancellation_and_reports_io_errors() {
        let volume = synthetic_volume(&[(5, root_record())]);
        let reader = |offset: u64, buffer: &mut [u8]| {
            let start = offset as usize;
            buffer.copy_from_slice(&volume[start..start + buffer.len()]);
            Ok(())
        };
        assert_eq!(read_mft(reader, |_, _| false).unwrap_err(), RawError::Cancelled);
        assert_eq!(read_mft(|_, _| Err("disque".to_string()), |_, _| true).unwrap_err(), RawError::Io("disque".into()));
    }
}
