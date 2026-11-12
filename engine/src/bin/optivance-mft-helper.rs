//! Helper MFT **expérimental** d'OPTIVANCE (D-06).
//!
//! Lancé à chaque analyse MFT, avec élévation UAC, par l'interface non élevée. Il :
//! - n'accepte qu'une lettre de volume et un nom de pipe au format de l'application ;
//! - vérifie que le volume est NTFS, l'ouvre en lecture seule et énumère la MFT par
//!   `FSCTL_ENUM_USN_DATA` (aucune lecture ni modification du journal USN) ;
//! - lit tailles, nombre de liens et type de réanalyse par `OpenFileById` (attributs
//!   seulement, sans rappel de données), puis complète les noms de hardlink par
//!   `FindFirstFileNameW` ;
//! - renvoie l'index au protocole v2 du scan classique, puis se termine.
//! La seule commande acceptée sur le pipe est l'annulation.
//!
//! Tout résultat incomplet ou incohérent est signalé `interrupted` avec `"partial":true` :
//! il n'est jamais présenté comme une analyse terminée.
//!
//! Correspondance des compteurs de profil (format v2 partagé avec le scan classique) :
//! enumerationMs = énumération MFT ; fileInformationMs = métadonnées par référence ;
//! metadataMs = complétion des hardlinks ; sortingMs = reconstruction de l'index.

#![forbid(unsafe_code)]

use std::fmt::Write as _;
use std::fs::{File, OpenOptions};
use std::io::BufWriter;
use std::os::windows::fs::OpenOptionsExt;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

use optivance_engine::mft::args::parse_helper_args;
use optivance_engine::mft::assemble::{assemble, SizeStats};
use optivance_engine::mft::enumerate::{enumerate_with, EnumError, ERROR_ACCESS_DENIED};
use optivance_engine::mft::index::{build_index, Diagnostics};
use optivance_engine::mft::links::{complete_hard_links, LinkStats};
use optivance_engine::mft::metadata::{collect, Collected};
use optivance_engine::mft::volume::{
    fetch_mft_chunk, filesystem_name, hard_link_names, open_root, open_volume, query_file, restrict_dll_search_to_system32,
};
use optivance_engine::protocol::{
    aggregate_unique_sizes, poll_cancel, send, send_entries, send_error_with_code, send_progress, serialize_summary, warn,
    Counters, Entry, Profile,
};

const HELLO: &str = "{\"v\":2,\"type\":\"hello\",\"protocol\":\"optivance.scan\",\"protocolVersion\":2,\"method\":\"mft\"}";
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

    /// Appelé à chaque opération coûteuse : annulation tous les POLL_EVERY appels,
    /// progression (nombre d'opérations de l'étape en cours) tous les PROGRESS_EVERY.
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
    let root_id = u128::from(root.file_id);
    let volume = match open_volume(letter) {
        Ok(volume) => volume,
        Err(error) if error.raw_os_error() == Some(ERROR_ACCESS_DENIED) => {
            return Ok(channel.fail("access-denied", "Accès au volume refusé : l’analyse MFT exige l’élévation administrateur.")?);
        }
        Err(error) => return Ok(channel.fail("volume-unavailable", &format!("Ouverture du volume {root_name} impossible : {error}"))?),
    };

    // 1. Énumération MFT.
    let phase = Instant::now();
    let enumerated = enumerate_with(
        ENUM_BUFFER_BYTES,
        |start, buffer| fetch_mft_chunk(&volume, start, buffer),
        |records| {
            channel.progress(records as u64);
            channel.poll()
        },
    );
    drop(volume);
    channel.profile.enumeration_ns = phase.elapsed().as_nanos();
    let mut records = match enumerated {
        Ok(records) => records,
        Err(EnumError::Cancelled) => return send_interrupted_root(&mut channel, &root_name),
        Err(error) => return Ok(channel.fail(error.code(), &error.to_string())?),
    };
    let record_count = records.len();

    // 2. Métadonnées par référence, classement des points de réanalyse.
    let phase = Instant::now();
    channel.calls = 0;
    let collected = collect(&mut records, |file_id, want_tag| {
        if !channel.tick() {
            return Err(std::io::Error::other("annulé"));
        }
        query_file(&root, file_id, want_tag)
    });
    channel.profile.file_information_ns = phase.elapsed().as_nanos();
    channel.profile.file_information_lookups = collected.queries;
    drop(root);
    if channel.is_cancelled() {
        return send_interrupted_root(&mut channel, &root_name);
    }

    // 3. Noms de hardlink absents de l'énumération.
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
        return send_interrupted_root(&mut channel, &root_name);
    }

    // 4. Reconstruction de l'arborescence.
    let phase = Instant::now();
    let index = build_index(root_id, &root_name, records);
    channel.profile.sorting_ns = phase.elapsed().as_nanos();

    // 5. Assemblage (aucune nouvelle ouverture) et agrégation.
    let mut assembled = assemble(&index, |file_id| collected.sizes.get(&file_id).copied().ok_or(()));
    let phase = Instant::now();
    aggregate_unique_sizes(&mut assembled.entries);
    channel.profile.aggregation_ns = phase.elapsed().as_nanos();

    // 6. Avertissements agrégés, puis résultats.
    let mut counters = assembled.counters;
    let report = Report { diagnostics: &index.diagnostics, collected: &collected, links: &links, sizes: &assembled.stats };
    let root_path = Path::new(&root_name);
    for (count, message) in report.problems() {
        if count > 0 {
            warn(&mut channel.writer, &mut channel.profile, &mut counters, root_path, &format!("MFT : {count} {message}"))?;
        }
    }
    let partial = report.is_partial();
    send_progress(&mut channel.writer, &mut channel.profile, &counters, started.elapsed().as_millis() as u64)?;
    let complete = send_entries(&mut channel.writer, &mut channel.profile, &assembled.entries, &channel.cancelled, &mut channel.commands)?;
    let state = if channel.is_cancelled() || !complete || partial { "interrupted" } else { "completed" };
    let extra = report.fields(partial, record_count);
    let total_ms = started.elapsed().as_secs_f64() * 1000.0;
    let summary = serialize_summary(state, &counters, &assembled.entries[0], &channel.profile, total_ms, &extra);
    send(&mut channel.writer, &mut channel.profile, &summary)?;
    Ok(())
}

struct Report<'a> {
    diagnostics: &'a Diagnostics,
    collected: &'a Collected,
    links: &'a LinkStats,
    sizes: &'a SizeStats,
}

impl Report<'_> {
    fn problems(&self) -> [(u64, &'static str); 10] {
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
        ]
    }

    fn is_partial(&self) -> bool {
        !self.diagnostics.is_consistent()
            || self.collected.failures > 0
            || self.sizes.failures > 0
            || self.sizes.missing_link_names > 0
            || self.links.failures > 0
            || self.links.unresolved_names > 0
    }

    fn fields(&self, partial: bool, records: usize) -> String {
        let (d, c, l, s) = (self.diagnostics, self.collected, self.links, self.sizes);
        let mut tags = String::new();
        for (index, (tag, count)) in c.reparse.by_tag.iter().enumerate() {
            let _ = write!(tags, "{}\"0x{tag:08X}\":{count}", if index == 0 { "" } else { "," });
        }
        format!(
            ",\"method\":\"mft\",\"partial\":{partial},\"mft\":{{\"records\":{records},\"unreachable\":{},\"staleParents\":{},\"conflictingKinds\":{},\"extraDirectoryLinks\":{},\"duplicateRecords\":{},\"reparsePointsSkipped\":{},\"reparseFollowed\":{},\"reparseTags\":{{{tags}}},\"metadataQueries\":{},\"metadataFailures\":{},\"sizeFailures\":{},\"linkFilesQueried\":{},\"linkNamesAdded\":{},\"linkUnresolved\":{},\"linkFailures\":{},\"missingLinkNames\":{}}}",
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
    };
    let entries = [root];
    send_entries(&mut channel.writer, &mut channel.profile, &entries, &channel.cancelled, &mut channel.commands)?;
    let counters = Counters { files: 0, directories: 1, warnings: 0, unique_logical_bytes: 0 };
    let total_ms = channel.started.elapsed().as_secs_f64() * 1000.0;
    let summary = serialize_summary("interrupted", &counters, &entries[0], &channel.profile, total_ms, ",\"method\":\"mft\",\"partial\":true");
    send(&mut channel.writer, &mut channel.profile, &summary)?;
    Ok(())
}
