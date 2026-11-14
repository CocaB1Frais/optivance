//! Protocole Named Pipe v3 partagé par le scan classique et le helper MFT.
//!
//! Code extrait sans changement de comportement de `main.rs` (V0.2). Le format des
//! messages est figé par les tests ci-dessous : le client WPF (`ScanEngineClient`)
//! rejette tout type de message inconnu, mais ignore les champs supplémentaires.

use std::ffi::c_void;
use std::fmt::Write as FmtWrite;
use std::fs::File;
use std::io::{BufWriter, Write};
use std::os::windows::io::AsRawHandle;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

pub const RESULT_BATCH_SIZE: usize = 512;
const MAX_WARNING_MESSAGES: u64 = 100;

type Handle = *mut c_void;

#[link(name = "kernel32")]
unsafe extern "system" {
    fn PeekNamedPipe(
        pipe: Handle,
        buffer: *mut c_void,
        buffer_size: u32,
        bytes_read: *mut u32,
        total_bytes_available: *mut u32,
        bytes_left_this_message: *mut u32,
    ) -> i32;
    fn ReadFile(file: Handle, buffer: *mut c_void, bytes_to_read: u32, bytes_read: *mut u32, overlapped: *mut c_void) -> i32;
}

pub struct Entry {
    pub name: String,
    pub parent_id: i32,
    pub child_start: i32,
    pub child_count: i32,
    pub is_directory: bool,
    pub logical_bytes: u64,
    pub allocated_bytes: u64,
    pub unique_logical_bytes: u64,
    pub unique_allocated_bytes: u64,
    pub hard_link_duplicate: bool,
    /// Dernière modification (FILETIME UTC) ; 0 si inconnue.
    pub modified: u64,
}

pub struct Counters {
    pub files: u64,
    pub directories: u64,
    pub warnings: u64,
    pub unique_logical_bytes: u64,
}

#[derive(Default)]
pub struct Profile {
    pub enumeration_ns: u128,
    pub sorting_ns: u128,
    pub metadata_ns: u128,
    pub file_information_ns: u128,
    pub aggregation_ns: u128,
    pub serialization_ns: u128,
    pub pipe_write_ns: u128,
    pub metadata_lookups: u64,
    pub file_information_lookups: u64,
    pub entries_sent: u64,
    pub result_batches: u64,
    pub bytes_sent: u64,
}

/// Remonte les tailles uniques de chaque entrée vers ses parents.
/// Suppose l'invariant de l'index plat : l'indice d'un parent est inférieur à celui de ses enfants.
pub fn aggregate_unique_sizes(entries: &mut [Entry]) {
    for index in (1..entries.len()).rev() {
        if let Ok(parent) = usize::try_from(entries[index].parent_id) {
            let logical = entries[index].unique_logical_bytes;
            let allocated = entries[index].unique_allocated_bytes;
            entries[parent].unique_logical_bytes = entries[parent].unique_logical_bytes.saturating_add(logical);
            entries[parent].unique_allocated_bytes = entries[parent].unique_allocated_bytes.saturating_add(allocated);
        }
    }
}

pub fn poll_cancel(
    writer: &mut BufWriter<File>,
    profile: &mut Profile,
    cancelled: &AtomicBool,
    command_buffer: &mut Vec<u8>,
) -> std::io::Result<()> {
    if cancelled.load(Ordering::Relaxed) {
        return Ok(());
    }

    let handle = writer.get_ref().as_raw_handle() as Handle;
    let mut available = 0u32;
    // SAFETY: handle is the connected named-pipe handle; null optional buffers are valid when their sizes are zero.
    let peeked = unsafe {
        PeekNamedPipe(
            handle,
            std::ptr::null_mut(),
            0,
            std::ptr::null_mut(),
            &mut available,
            std::ptr::null_mut(),
        )
    };
    if peeked == 0 {
        return Err(std::io::Error::last_os_error());
    }
    if available == 0 {
        return Ok(());
    }

    let mut received = [0u8; 4096];
    let read_capacity = available.min(received.len() as u32);
    let mut bytes_read = 0u32;
    // SAFETY: received has read_capacity writable bytes and the handle is a connected pipe.
    let read_ok = unsafe {
        ReadFile(
            handle,
            received.as_mut_ptr().cast(),
            read_capacity,
            &mut bytes_read,
            std::ptr::null_mut(),
        )
    };
    if read_ok == 0 {
        return Err(std::io::Error::last_os_error());
    }
    command_buffer.extend_from_slice(&received[..bytes_read as usize]);
    if command_buffer.windows(b"\"type\":\"cancel\"".len()).any(|window| window == b"\"type\":\"cancel\"") {
        cancelled.store(true, Ordering::Relaxed);
    }
    if let Some(newline) = command_buffer.iter().rposition(|byte| *byte == b'\n') {
        command_buffer.drain(..=newline);
    }
    let _ = profile;
    Ok(())
}

pub fn send(writer: &mut BufWriter<File>, profile: &mut Profile, message: &str) -> std::io::Result<()> {
    profile.bytes_sent = profile.bytes_sent.saturating_add(message.len() as u64 + 1);
    let started = Instant::now();
    writer.write_all(message.as_bytes())?;
    writer.write_all(b"\n")?;
    writer.flush()?;
    profile.pipe_write_ns += started.elapsed().as_nanos();
    Ok(())
}

pub fn send_error(writer: &mut BufWriter<File>, profile: &mut Profile, message: &str) -> std::io::Result<()> {
    send_error_with_code(writer, profile, message, None)
}

/// Erreur avec un code machine facultatif (champ supplémentaire ignoré par les anciens clients).
pub fn send_error_with_code(
    writer: &mut BufWriter<File>,
    profile: &mut Profile,
    message: &str,
    code: Option<&str>,
) -> std::io::Result<()> {
    let started = Instant::now();
    let json = serialize_error(message, code);
    profile.serialization_ns += started.elapsed().as_nanos();
    send(writer, profile, &json)
}

pub fn serialize_error(message: &str, code: Option<&str>) -> String {
    let mut json = String::with_capacity(message.len() + 64);
    json.push_str("{\"v\":3,\"type\":\"error\",\"message\":");
    push_json_string(&mut json, message);
    if let Some(code) = code {
        json.push_str(",\"code\":");
        push_json_string(&mut json, code);
    }
    json.push('}');
    json
}

pub fn send_progress(writer: &mut BufWriter<File>, profile: &mut Profile, counters: &Counters, elapsed_ms: u64) -> std::io::Result<()> {
    let started = Instant::now();
    let json = format!(
        "{{\"v\":3,\"type\":\"progress\",\"visitedFiles\":{},\"visitedDirectories\":{},\"uniqueLogicalBytes\":{},\"elapsedMs\":{}}}",
        counters.files, counters.directories, counters.unique_logical_bytes, elapsed_ms
    );
    profile.serialization_ns += started.elapsed().as_nanos();
    send(writer, profile, &json)
}

pub fn send_entries(
    writer: &mut BufWriter<File>,
    profile: &mut Profile,
    entries: &[Entry],
    cancelled: &AtomicBool,
    command_buffer: &mut Vec<u8>,
) -> std::io::Result<bool> {
    for batch in entries.chunks(RESULT_BATCH_SIZE) {
        if profile.entries_sent > 0 {
            poll_cancel(writer, profile, cancelled, command_buffer)?;
        }
        if cancelled.load(Ordering::Relaxed) && profile.entries_sent > 0 {
            return Ok(false);
        }

        let batch_start = profile.entries_sent as usize;
        let serialization_started = Instant::now();
        let message = serialize_results(batch, batch_start);
        profile.serialization_ns += serialization_started.elapsed().as_nanos();
        send(writer, profile, &message)?;
        profile.entries_sent += batch.len() as u64;
        profile.result_batches += 1;
    }
    Ok(true)
}

/// Lot de résultats v3. L'identifiant de chaque entrée est implicite (`start` + position) ;
/// chaque entrée est une ligne compacte :
/// `[parentId, childStart, childCount, dossier(0|1), nom, logique, alloué, doublon(0|1), modifié]`.
/// Pour un dossier, logique/alloué sont les totaux uniques de son sous-arbre.
pub fn serialize_results(batch: &[Entry], batch_start: usize) -> String {
    let mut message = String::with_capacity(batch.len() * 64 + 64);
    let _ = write!(message, "{{\"v\":3,\"type\":\"results\",\"start\":{batch_start},\"entries\":[");
    for (offset, entry) in batch.iter().enumerate() {
        if offset != 0 {
            message.push(',');
        }
        let _ = write!(message, "[{},{},{},{},", entry.parent_id, entry.child_start, entry.child_count, u8::from(entry.is_directory));
        push_json_string(&mut message, &entry.name);
        let logical = if entry.is_directory { entry.unique_logical_bytes } else { entry.logical_bytes };
        let allocated = if entry.is_directory { entry.unique_allocated_bytes } else { entry.allocated_bytes };
        let _ = write!(message, ",{logical},{allocated},{},{}]", u8::from(entry.hard_link_duplicate), entry.modified);
    }
    message.push_str("]}");
    message
}

pub fn warn(
    writer: &mut BufWriter<File>,
    profile: &mut Profile,
    counters: &mut Counters,
    path: &Path,
    message: &str,
) -> std::io::Result<()> {
    counters.warnings += 1;
    if counters.warnings > MAX_WARNING_MESSAGES {
        return Ok(());
    }
    let started = Instant::now();
    let mut json = String::with_capacity(path.as_os_str().len() + message.len() + 96);
    json.push_str("{\"v\":3,\"type\":\"warning\",\"path\":");
    push_json_string(&mut json, &path.to_string_lossy());
    json.push_str(",\"message\":");
    push_json_string(&mut json, message);
    json.push('}');
    profile.serialization_ns += started.elapsed().as_nanos();
    send(writer, profile, &json)
}

/// `extra_fields` est inséré tel quel après les totaux (ex. `,"method":"mft"`) ; vide pour le scan classique.
pub fn serialize_summary(state: &str, counters: &Counters, root: &Entry, profile: &Profile, total_ms: f64, extra_fields: &str) -> String {
    format!(
        "{{\"v\":3,\"type\":\"{state}\",\"files\":{},\"directories\":{},\"warnings\":{},\"uniqueLogicalBytes\":{},\"uniqueAllocatedBytes\":{}{extra_fields},\"profile\":{{\"totalMs\":{total_ms:.3},\"enumerationMs\":{:.3},\"sortingMs\":{:.3},\"metadataMs\":{:.3},\"fileInformationMs\":{:.3},\"aggregationMs\":{:.3},\"serializationMs\":{:.3},\"pipeWriteMs\":{:.3},\"metadataLookups\":{},\"fileInformationLookups\":{},\"entriesSent\":{},\"resultBatches\":{},\"bytesSent\":{}}}}}",
        counters.files,
        counters.directories,
        counters.warnings,
        root.unique_logical_bytes,
        root.unique_allocated_bytes,
        nanos_to_ms(profile.enumeration_ns),
        nanos_to_ms(profile.sorting_ns),
        nanos_to_ms(profile.metadata_ns),
        nanos_to_ms(profile.file_information_ns),
        nanos_to_ms(profile.aggregation_ns),
        nanos_to_ms(profile.serialization_ns),
        nanos_to_ms(profile.pipe_write_ns),
        profile.metadata_lookups,
        profile.file_information_lookups,
        profile.entries_sent,
        profile.result_batches,
        profile.bytes_sent
    )
}

fn nanos_to_ms(nanos: u128) -> f64 {
    nanos as f64 / 1_000_000.0
}

pub fn push_json_string(output: &mut String, value: &str) {
    output.push('"');
    for character in value.chars() {
        match character {
            '"' => output.push_str("\\\""),
            '\\' => output.push_str("\\\\"),
            '\n' => output.push_str("\\n"),
            '\r' => output.push_str("\\r"),
            '\t' => output.push_str("\\t"),
            character if character < ' ' => {
                let _ = write!(output, "\\u{:04x}", character as u32);
            }
            character => output.push(character),
        }
    }
    output.push('"');
}

#[cfg(test)]
mod tests {
    use super::*;

    fn entry(name: &str, parent_id: i32, is_directory: bool, logical: u64, allocated: u64, duplicate: bool) -> Entry {
        Entry {
            name: name.into(),
            parent_id,
            child_start: 0,
            child_count: 0,
            is_directory,
            logical_bytes: logical,
            allocated_bytes: allocated,
            unique_logical_bytes: if duplicate { 0 } else { logical },
            unique_allocated_bytes: if duplicate { 0 } else { allocated },
            hard_link_duplicate: duplicate,
            modified: 0,
        }
    }

    #[test]
    fn json_string_escapes_quotes_slashes_and_control_characters() {
        let mut output = String::new();
        push_json_string(&mut output, "a\"b\\c\n\t");
        assert_eq!(output, "\"a\\\"b\\\\c\\n\\t\"");
    }

    #[test]
    fn results_batch_uses_compact_v3_rows() {
        let mut root = entry("R", -1, true, 0, 0, false);
        root.child_start = 1;
        root.child_count = 1;
        root.unique_logical_bytes = 10;
        root.unique_allocated_bytes = 4096;
        let mut file = entry("a\"b", 0, false, 10, 4096, true);
        file.modified = 133_456_789_012_345_678;
        let batch = [root, file];
        // [parentId, childStart, childCount, dossier, nom, logique, alloué, doublon, modifié(FILETIME)]
        assert_eq!(
            serialize_results(&batch, 7),
            "{\"v\":3,\"type\":\"results\",\"start\":7,\"entries\":[\
             [-1,1,1,1,\"R\",10,4096,0,0],\
             [0,0,0,0,\"a\\\"b\",10,4096,1,133456789012345678]]}"
        );
    }

    #[test]
    fn summary_keeps_wire_format_and_inserts_extra_fields() {
        let counters = Counters { files: 2, directories: 1, warnings: 0, unique_logical_bytes: 5 };
        let mut root = entry("R", -1, true, 0, 0, false);
        root.unique_logical_bytes = 5;
        root.unique_allocated_bytes = 8;
        let profile = Profile { entries_sent: 3, result_batches: 1, bytes_sent: 99, ..Profile::default() };
        let expected_tail = ",\"profile\":{\"totalMs\":1.500,\"enumerationMs\":0.000,\"sortingMs\":0.000,\"metadataMs\":0.000,\"fileInformationMs\":0.000,\"aggregationMs\":0.000,\"serializationMs\":0.000,\"pipeWriteMs\":0.000,\"metadataLookups\":0,\"fileInformationLookups\":0,\"entriesSent\":3,\"resultBatches\":1,\"bytesSent\":99}}";
        let head = "{\"v\":3,\"type\":\"completed\",\"files\":2,\"directories\":1,\"warnings\":0,\"uniqueLogicalBytes\":5,\"uniqueAllocatedBytes\":8";
        assert_eq!(serialize_summary("completed", &counters, &root, &profile, 1.5, ""), format!("{head}{expected_tail}"));
        assert_eq!(
            serialize_summary("completed", &counters, &root, &profile, 1.5, ",\"method\":\"mft\""),
            format!("{head},\"method\":\"mft\"{expected_tail}")
        );
    }

    #[test]
    fn error_carries_optional_machine_code() {
        assert_eq!(serialize_error("x", None), "{\"v\":3,\"type\":\"error\",\"message\":\"x\"}");
        assert_eq!(serialize_error("x", Some("not-ntfs")), "{\"v\":3,\"type\":\"error\",\"message\":\"x\",\"code\":\"not-ntfs\"}");
    }

    #[test]
    fn aggregation_adds_unique_sizes_up_to_every_ancestor() {
        let mut entries = vec![
            entry("R", -1, true, 0, 0, false),
            entry("d", 0, true, 0, 0, false),
            entry("f", 1, false, 10, 16, false),
            entry("lien", 0, false, 10, 16, true),
        ];
        aggregate_unique_sizes(&mut entries);
        assert_eq!((entries[0].unique_logical_bytes, entries[0].unique_allocated_bytes), (10, 16));
        assert_eq!((entries[1].unique_logical_bytes, entries[1].unique_allocated_bytes), (10, 16));
    }
}
