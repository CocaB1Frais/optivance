//! Helper MFT d'OPTIVANCE (D-06), lancé avec élévation UAC à chaque analyse MFT.
//!
//! - N'accepte qu'une lettre de volume et un nom de pipe au format de l'application.
//! - Vérifie que le volume est NTFS et l'ouvre en lecture seule.
//! - Méthode principale `mft-raw` : lecture directe de la `$MFT` (noms, liens, tailles,
//!   types de réanalyse et dates en une passe séquentielle).
//! - Méthode de secours `mft-enum` (si la `$MFT` a une forme non prise en charge) :
//!   `FSCTL_ENUM_USN_DATA` + `OpenFileById` + `FindFirstFileNameW`.
//! - Aucune lecture ni modification du journal USN, aucun contenu de fichier, aucune écriture.
//! - Renvoie l'index au protocole v3 du scan classique, puis se termine. La seule commande
//!   acceptée sur le pipe est l'annulation.
//!
//! Tout résultat incomplet ou incohérent est signalé `interrupted` avec `"partial":true`.
//!
//! Compteurs de profil (format v3) : enumerationMs = lecture/énumération MFT ;
//! fileInformationMs = métadonnées ; metadataMs = complétion des hardlinks (secours) ;
//! sortingMs = reconstruction de l'index.

#![forbid(unsafe_code)]

use optivance_engine::fasthash::FastMap as HashMap;
use std::fmt::Write as _;
use std::fs::{File, OpenOptions};
use std::io::BufWriter;
use std::os::windows::fs::OpenOptionsExt;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

use optivance_engine::mft::args::parse_helper_args;
use optivance_engine::mft::assemble::{assemble, FileSizes, SizeStats};
use optivance_engine::mft::enumerate::{enumerate_with, EnumError, ERROR_ACCESS_DENIED};
use optivance_engine::mft::index::{build_index, Diagnostics};
use optivance_engine::mft::links::{complete_hard_links, LinkStats};
use optivance_engine::mft::metadata::{collect, Collected, FileInfo};
use optivance_engine::mft::raw::{read_mft, RawError, RawStats};
use optivance_engine::mft::records::UsnRecord;
use optivance_engine::mft::volume::{
    fetch_mft_chunk, filesystem_name, hard_link_names, open_root, open_volume, query_file, read_volume_at,
    restrict_dll_search_to_system32, RootDirectory,
};
use optivance_engine::protocol::{
    aggregate_unique_sizes, poll_cancel, send, send_entries, send_error_with_code, send_progress, serialize_summary, warn,
    Counters, Entry, Profile,
};

const HELLO: &str = "{\"v\":3,\"type\":\"hello\",\"protocol\":\"optivance.scan\",\"protocolVersion\":3,\"method\":\"mft\"}";
const ENUM_BUFFER_BYTES: usize = 1 << 20;
const POLL_EVERY: u64 = 1_024;
const PROGRESS_EVERY: u64 = 16_384;
/// SecurityIdentification << 16 : le serveur du pipe ne peut pas emprunter le jeton élevé.
const SECURITY_IDENTIFICATION: u32 = 0x0001_0000;

struct Channel {
    writer: BufWriter<File>,
    profile: Profile,
    cancelled: AtomicBool,
    commands: Vec<u8>,
    started: Instant,
    calls: u64,
}

impl Channel {
    fn poll(&mut self) -> bool {
        if poll_cancel(&mut self.writer, &mut self.profile, &self.cancelled, &mut self.commands).is_err() {
            // Canal rompu : l'interface n'écoute plus, on s'arrête comme pour une annulation.
            self.cancelled.store(true, Ordering::Relaxed);
        }
        !self.cancelled.load(Ordering::Relaxed)
    }

    fn is_cancelled(&self) -> bool {
        self.cancelled.load(Ordering::Relaxed)
    }

    /// Annulation tous les POLL_EVERY appels, progression tous les PROGRESS_EVERY.
    fn tick(&mut self) -> bool {
        self.calls += 1;
        if self.calls % PROGRESS_EVERY == 0 {
            self.progress(self.calls);
        }
        if self.calls % POLL_EVERY == 0 { self.poll() } else { !self.is_cancelled() }
    }

    fn progress(&mut self, visited: u64) {
        let counters = Counters { files: visited, directories: 0, warnings: 0, unique_logical_bytes: 0 };
        let elapsed = self.started.elapsed().as_millis() as u64;
        let _ = send_progress(&mut self.writer, &mut self.profile, &counters, elapsed);
    }

    fn fail(&mut self, code: &str, message: &str) -> std::io::Result<()> {
        send_error_with_code(&mut self.writer, &mut self.profile, message, Some(code))
    }
}

/// Résultat d'une méthode MFT, avant reconstruction de l'arborescence.
struct Prepared {
    method: &'static str,
    records: Vec<UsnRecord>,
    record_count: usize,
    collected: Collected,
    links: LinkStats,
    raw: RawStats,
    /// Dates de modification par référence (lecture directe seulement).
    modified: HashMap<u128, u64>,
    fallback_reason: Option<String>,
}

enum Outcome {
    Ready(Prepared),
    Cancelled,
    Failed(&'static str, String),
}

fn main() {
    if let Err(error) = restrict_dll_search_to_system32() {
        eprintln!("optivance-mft-helper: restriction des DLL impossible : {error}");
        std::process::exit(3);
    }
    if let Err(error) = run() {
        eprintln!("optivance-mft-helper: {error}");
        std::process::exit(2);
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let started = Instant::now();
    let args = parse_helper_args(std::env::args().skip(1))?;
    let pipe = OpenOptions::new()
        .read(true)
        .write(true)
        .security_qos_flags(SECURITY_IDENTIFICATION)
        .open(format!(r"\\.\pipe\{}", args.pipe_name))?;
    let mut channel = Channel {
        writer: BufWriter::with_capacity(64 * 1024, pipe),
        profile: Profile::default(),
        cancelled: AtomicBool::new(false),
        commands: Vec::with_capacity(64),
        started,
        calls: 0,
    };
    send(&mut channel.writer, &mut channel.profile, HELLO)?;

    let letter = args.volume;
    let root_name = format!("{letter}:\\");
    match filesystem_name(letter) {
        Ok(name) if name == "NTFS" => {}
        Ok(name) => return Ok(channel.fail("not-ntfs", &format!("Le volume {root_name} est en {name} ; l’analyse MFT exige NTFS."))?),
        Err(error) => return Ok(channel.fail("volume-unavailable", &format!("Volume {root_name} indisponible : {error}"))?),
    }
    let root = match open_root(letter) {
        Ok(root) => root,
        Err(error) => return Ok(channel.fail("volume-unavailable", &format!("Racine {root_name} inaccessible : {error}"))?),
    };
    let volume = match open_volume(letter) {
        Ok(volume) => File::from(volume),
        Err(error) if error.raw_os_error() == Some(ERROR_ACCESS_DENIED) => {
            return Ok(channel.fail("access-denied", "Accès au volume refusé : l’analyse MFT exige l’élévation administrateur.")?);
        }
        Err(error) => return Ok(channel.fail("volume-unavailable", &format!("Ouverture du volume {root_name} impossible : {error}"))?),
    };
    let root_id = u128::from(root.file_id);

    let prepared = match scan_raw(&mut channel, &volume, letter) {
        Outcome::Ready(prepared) => prepared,
        Outcome::Cancelled => return send_interrupted_root(&mut channel, &root_name),
        Outcome::Failed(_, reason) => match scan_enum(&mut channel, &volume, &root, letter, root_id) {
            Outcome::Ready(mut prepared) => {
                prepared.fallback_reason = Some(reason);
                prepared
            }
            Outcome::Cancelled => return send_interrupted_root(&mut channel, &root_name),
            Outcome::Failed(code, message) => return Ok(channel.fail(code, &message)?),
        },
    };
    drop(volume);
    drop(root);
    finish(&mut channel, prepared, root_id, &root_name)
}

/// Méthode principale : lecture séquentielle de la `$MFT`.
fn scan_raw(channel: &mut Channel, volume: &File, letter: char) -> Outcome {
    // Un handle par lecture simultanée : Windows sérialise les lectures d'un même handle synchrone.
    let mut handles: Vec<File> = (0..7).filter_map(|_| open_volume(letter).ok().map(File::from)).collect();
    if let Ok(copy) = volume.try_clone() {
        handles.push(copy);
    }
    let pool = std::sync::Mutex::new(handles);
    let phase = Instant::now();
    let read = read_mft(
        |offset, buffer| {
            let handle = pool.lock().map_err(|_| "verrou".to_string())?.pop();
            let Some(handle) = handle else { return read_volume_at(volume, offset, buffer) };
            let result = read_volume_at(&handle, offset, buffer);
            if let Ok(mut free) = pool.lock() {
                free.push(handle);
            }
            result
        },
        |done, _total| {
            if done % (PROGRESS_EVERY * 8) < 4096 {
                channel.progress(done);
            }
            channel.poll()
        },
    );
    channel.profile.enumeration_ns = phase.elapsed().as_nanos();
    let raw = match read {
        Ok(raw) => raw,
        Err(RawError::Cancelled) => return Outcome::Cancelled,
        Err(error) => return Outcome::Failed("raw-failed", error.to_string()),
    };
    // metadataMs (inutilisé par cette méthode) = durée de la fusion finale, incluse dans enumerationMs.
    channel.profile.metadata_ns = (raw.stats.finish_ms * 1_000_000.0) as u128;

    let phase = Instant::now();
    let mut records = raw.records;
    let record_count = records.len();
    let (sizes, tags, modified) = (raw.sizes, raw.reparse_tags, raw.modified);
    let collected = collect(&mut records, |file_id, want_tag| {
        let sizes = sizes.get(&file_id).copied().unwrap_or(FileSizes { logical: 0, allocated: 0, links: 1 });
        let reparse_tag = if want_tag { tags.get(&file_id).copied() } else { None };
        Ok::<_, ()>(FileInfo { sizes, reparse_tag })
    });
    channel.profile.file_information_ns = phase.elapsed().as_nanos();
    channel.profile.file_information_lookups = collected.queries;
    Outcome::Ready(Prepared {
        method: "mft-raw",
        records,
        record_count,
        collected,
        links: LinkStats::default(),
        raw: raw.stats,
        modified,
        fallback_reason: None,
    })
}

/// Méthode de secours : énumération FSCTL, ouvertures par référence, noms de hardlink.
fn scan_enum(channel: &mut Channel, volume: &File, root: &RootDirectory, letter: char, root_id: u128) -> Outcome {
    let phase = Instant::now();
    let enumerated = enumerate_with(
        ENUM_BUFFER_BYTES,
        |start, buffer| fetch_mft_chunk(volume, start, buffer),
        |records| {
            channel.progress(records as u64);
            channel.poll()
        },
    );
    channel.profile.enumeration_ns = phase.elapsed().as_nanos();
    let mut records = match enumerated {
        Ok(records) => records,
        Err(EnumError::Cancelled) => return Outcome::Cancelled,
        Err(error) => return Outcome::Failed(error.code(), error.to_string()),
    };
    let record_count = records.len();

    let phase = Instant::now();
    channel.calls = 0;
    let collected = collect(&mut records, |file_id, want_tag| {
        if !channel.tick() {
            return Err(std::io::Error::other("annulé"));
        }
        query_file(root, file_id, want_tag)
    });
    channel.profile.file_information_ns = phase.elapsed().as_nanos();
    channel.profile.file_information_lookups = collected.queries;
    if channel.is_cancelled() {
        return Outcome::Cancelled;
    }

    let phase = Instant::now();
    channel.calls = 0;
    let links = complete_hard_links(&mut records, root_id, &collected.sizes, |path| {
        if !channel.tick() {
            return Err(std::io::Error::other("annulé"));
        }
        hard_link_names(letter, path)
    });
    channel.profile.metadata_ns = phase.elapsed().as_nanos();
    channel.profile.metadata_lookups = links.files_queried;
    if channel.is_cancelled() {
        return Outcome::Cancelled;
    }
    Outcome::Ready(Prepared {
        method: "mft-enum",
        records,
        record_count,
        collected,
        links,
        raw: RawStats::default(),
        modified: HashMap::default(),
        fallback_reason: None,
    })
}

fn finish(channel: &mut Channel, prepared: Prepared, root_id: u128, root_name: &str) -> Result<(), Box<dyn std::error::Error>> {
    let phase = Instant::now();
    let Prepared { method, records, record_count, collected, links, raw, modified, fallback_reason } = prepared;
    let index = build_index(root_id, root_name, records);
    channel.profile.sorting_ns = phase.elapsed().as_nanos();

    let mut assembled = assemble(&index, |file_id| collected.sizes.get(&file_id).copied().ok_or(()));
    for (entry, source) in assembled.entries.iter_mut().zip(&index.entries) {
        entry.modified = modified.get(&source.file_id).copied().unwrap_or(0);
    }
    let phase = Instant::now();
    aggregate_unique_sizes(&mut assembled.entries);
    channel.profile.aggregation_ns = phase.elapsed().as_nanos();

    let mut counters = assembled.counters;
    let report = Report { diagnostics: &index.diagnostics, collected: &collected, links: &links, sizes: &assembled.stats, raw: &raw };
    let root_path = Path::new(root_name);
    if let Some(reason) = &fallback_reason {
        warn(&mut channel.writer, &mut channel.profile, &mut counters, root_path, &format!("MFT : lecture directe impossible ({reason}) ; énumération de secours utilisée."))?;
    }
    for (count, message) in report.problems() {
        if count > 0 {
            warn(&mut channel.writer, &mut channel.profile, &mut counters, root_path, &format!("MFT : {count} {message}"))?;
        }
    }
    let partial = report.is_partial();
    send_progress(&mut channel.writer, &mut channel.profile, &counters, channel.started.elapsed().as_millis() as u64)?;
    let complete = send_entries(&mut channel.writer, &mut channel.profile, &assembled.entries, &channel.cancelled, &mut channel.commands)?;
    let state = if channel.is_cancelled() || !complete || partial { "interrupted" } else { "completed" };
    let extra = report.fields(method, partial, record_count);
    let total_ms = channel.started.elapsed().as_secs_f64() * 1000.0;
    let summary = serialize_summary(state, &counters, &assembled.entries[0], &channel.profile, total_ms, &extra);
    send(&mut channel.writer, &mut channel.profile, &summary)?;
    Ok(())
}

struct Report<'a> {
    diagnostics: &'a Diagnostics,
    collected: &'a Collected,
    links: &'a LinkStats,
    sizes: &'a SizeStats,
    raw: &'a RawStats,
}

impl Report<'_> {
    fn problems(&self) -> [(u64, &'static str); 11] {
        let d = self.diagnostics;
        [
            (d.unreachable_files, "référence(s) non rattachée(s) à la racine (parent absent, périmé ou cycle)"),
            (d.stale_parent_references, "référence(s) de parent périmée(s)"),
            (d.conflicting_kinds, "référence(s) vue(s) à la fois comme dossier et fichier"),
            (d.extra_directory_links, "lien(s) supplémentaire(s) vers un dossier déjà placé"),
            (d.duplicate_records, "enregistrement(s) en double ignoré(s)"),
            (d.reparse_points_skipped, "lien(s) symbolique(s), jonction(s) ou point(s) de montage ignoré(s), comme le scan classique"),
            (self.collected.failures, "référence(s) dont les métadonnées n’ont pas pu être lues"),
            (self.sizes.failures, "fichier(s) indexé(s) sans taille lisible (comptés 0 octet)"),
            (self.links.failures + self.links.unresolved_names, "nom(s) de hardlink non résolu(s)"),
            (self.sizes.missing_link_names, "nom(s) de hardlink déclaré(s) par NTFS mais absent(s) de l’index"),
            (self.raw.corrupt, "enregistrement(s) $MFT illisible(s) (volume modifié pendant la lecture ?)"),
        ]
    }

    fn is_partial(&self) -> bool {
        !self.diagnostics.is_consistent()
            || self.collected.failures > 0
            || self.sizes.failures > 0
            || self.sizes.missing_link_names > 0
            || self.links.failures > 0
            || self.links.unresolved_names > 0
            || self.raw.corrupt > 0
    }

    fn fields(&self, method: &str, partial: bool, records: usize) -> String {
        let (d, c, l, s, r) = (self.diagnostics, self.collected, self.links, self.sizes, self.raw);
        let mut tags = String::new();
        for (index, (tag, count)) in c.reparse.by_tag.iter().enumerate() {
            let _ = write!(tags, "{}\"0x{tag:08X}\":{count}", if index == 0 { "" } else { "," });
        }
        format!(
            ",\"method\":\"mft\",\"mftMethod\":\"{method}\",\"partial\":{partial},\"mft\":{{\"records\":{records},\"mftRecordsRead\":{},\"mftInUse\":{},\"mftCorrupt\":{},\"orphanExtensions\":{},\"systemExcluded\":{},\"unreachable\":{},\"staleParents\":{},\"conflictingKinds\":{},\"extraDirectoryLinks\":{},\"duplicateRecords\":{},\"reparsePointsSkipped\":{},\"reparseFollowed\":{},\"reparseTags\":{{{tags}}},\"metadataQueries\":{},\"metadataFailures\":{},\"sizeFailures\":{},\"linkFilesQueried\":{},\"linkNamesAdded\":{},\"linkUnresolved\":{},\"linkFailures\":{},\"missingLinkNames\":{}}}",
            r.records_read,
            r.in_use,
            r.corrupt,
            r.orphan_extensions,
            r.system_excluded,
            d.unreachable_files,
            d.stale_parent_references,
            d.conflicting_kinds,
            d.extra_directory_links,
            d.duplicate_records,
            d.reparse_points_skipped,
            c.reparse.followed,
            c.queries,
            c.failures,
            s.failures,
            l.files_queried,
            l.names_added,
            l.unresolved_names,
            l.failures,
            s.missing_link_names,
        )
    }
}

/// Annulation avant l'index : seule la racine est transmise, état `interrupted`.
fn send_interrupted_root(channel: &mut Channel, root_name: &str) -> Result<(), Box<dyn std::error::Error>> {
    let root = Entry {
        name: root_name.to_owned(),
        parent_id: -1,
        child_start: 1,
        child_count: 0,
        is_directory: true,
        logical_bytes: 0,
        allocated_bytes: 0,
        unique_logical_bytes: 0,
        unique_allocated_bytes: 0,
        hard_link_duplicate: false,
        modified: 0,
    };
    let entries = [root];
    send_entries(&mut channel.writer, &mut channel.profile, &entries, &channel.cancelled, &mut channel.commands)?;
    let counters = Counters { files: 0, directories: 1, warnings: 0, unique_logical_bytes: 0 };
    let total_ms = channel.started.elapsed().as_secs_f64() * 1000.0;
    let summary = serialize_summary("interrupted", &counters, &entries[0], &channel.profile, total_ms, ",\"method\":\"mft\",\"partial\":true");
    send(&mut channel.writer, &mut channel.profile, &summary)?;
    Ok(())
}
