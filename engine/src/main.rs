#![forbid(unsafe_op_in_unsafe_fn)]

use std::collections::HashSet;
use std::ffi::c_void;
use std::fs::{self, OpenOptions};
use std::io::BufWriter;
use std::os::windows::ffi::OsStrExt;
use std::os::windows::fs::MetadataExt;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

use optivance_engine::protocol::{
    aggregate_unique_sizes, poll_cancel, send, send_entries, send_error, send_progress, serialize_summary, warn, Counters,
    Entry, Profile,
};

const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0000_0400;
const FILE_FLAG_BACKUP_SEMANTICS: u32 = 0x0200_0000;
const FILE_READ_ATTRIBUTES: u32 = 0x0080;
const FILE_SHARE_READ: u32 = 0x0000_0001;
const FILE_SHARE_WRITE: u32 = 0x0000_0002;
const FILE_SHARE_DELETE: u32 = 0x0000_0004;
const OPEN_EXISTING: u32 = 3;
const FILE_STANDARD_INFO_CLASS: i32 = 1;
const FILE_ID_INFO_CLASS: i32 = 18;
const MAX_PATH_UNITS: usize = 260;
const CANCEL_POLL_EVERY_ENTRIES: u64 = 128;
const PROGRESS_EVERY_ENTRIES: u64 = 2_048;

type Handle = *mut c_void;

#[link(name = "kernel32")]
unsafe extern "system" {
    fn CreateFileW(
        file_name: *const u16,
        desired_access: u32,
        share_mode: u32,
        security_attributes: *mut c_void,
        creation_disposition: u32,
        flags_and_attributes: u32,
        template_file: Handle,
    ) -> Handle;
    fn GetFileInformationByHandleEx(file: Handle, class: i32, info: *mut c_void, size: u32) -> i32;
    fn GetFileInformationByHandle(file: Handle, info: *mut ByHandleFileInformation) -> i32;
    fn CloseHandle(object: Handle) -> i32;
}

#[repr(C)]
#[derive(Default)]
struct FileStandardInfo {
    allocation_size: i64,
    end_of_file: i64,
    number_of_links: u32,
    delete_pending: u8,
    directory: u8,
    reserved: [u8; 2],
}

#[repr(C)]
#[derive(Default)]
struct FileIdInfo {
    volume_serial_number: u64,
    file_id: [u8; 16],
}

#[repr(C)]
#[derive(Default)]
struct FileTime {
    low: u32,
    high: u32,
}

#[repr(C)]
#[derive(Default)]
struct ByHandleFileInformation {
    file_attributes: u32,
    creation_time: FileTime,
    last_access_time: FileTime,
    last_write_time: FileTime,
    volume_serial_number: u32,
    file_size_high: u32,
    file_size_low: u32,
    number_of_links: u32,
    file_index_high: u32,
    file_index_low: u32,
}

#[derive(Clone, Copy, Hash, Eq, PartialEq)]
struct FileIdentity(u64, [u8; 16]);


struct FileDetails {
    identity: Option<FileIdentity>,
    allocation_size: u64,
    identity_expected: bool,
}

fn main() {
    if let Err(error) = run() {
        eprintln!("optivance-engine: {error}");
        std::process::exit(2);
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let total_started = Instant::now();
    let (pipe_name, target) = parse_args()?;
    let pipe_path = format!(r"\\.\pipe\{pipe_name}");
    let pipe = OpenOptions::new().read(true).write(true).open(pipe_path)?;
    let mut writer = BufWriter::with_capacity(64 * 1024, pipe);
    let mut profile = Profile::default();
    send(
        &mut writer,
        &mut profile,
        "{\"v\":2,\"type\":\"hello\",\"protocol\":\"optivance.scan\",\"protocolVersion\":2}",
    )?;

    let cancelled = AtomicBool::new(false);
    let mut command_buffer = Vec::with_capacity(64);
    let root = PathBuf::from(target);
    let metadata_started = Instant::now();
    let root_metadata = fs::symlink_metadata(&root);
    profile.metadata_ns += metadata_started.elapsed().as_nanos();
    profile.metadata_lookups += 1;
    let metadata = match root_metadata {
        Ok(metadata) if metadata.is_dir() => metadata,
        Ok(_) => {
            send_error(&mut writer, &mut profile, "La cible n’est pas un dossier ou un volume lisible.")?;
            return Ok(());
        }
        Err(error) => {
            send_error(&mut writer, &mut profile, &format!("Impossible d’ouvrir la cible : {error}"))?;
            return Ok(());
        }
    };
    if metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
        send_error(&mut writer, &mut profile, "La cible est un point de réanalyse; le moteur ne le suit pas.")?;
        return Ok(());
    }

    let mut entries = vec![Entry {
        name: root.file_name().unwrap_or(root.as_os_str()).to_string_lossy().into_owned(),
        parent_id: -1,
        child_start: 1,
        child_count: 0,
        is_directory: true,
        logical_bytes: 0,
        allocated_bytes: 0,
        unique_logical_bytes: 0,
        unique_allocated_bytes: 0,
        hard_link_duplicate: false,
    }];
    let mut pending = vec![(root, 0usize)];
    let mut hard_link_identities: HashSet<FileIdentity> = HashSet::new();
    let mut counters = Counters {
        files: 0,
        directories: 1,
        warnings: 0,
        unique_logical_bytes: 0,
    };
    let mut last_progress = 0u64;
    let mut last_cancel_poll = 0u64;

    while let Some((directory, parent_index)) = pending.pop() {
        poll_cancel(&mut writer, &mut profile, &cancelled, &mut command_buffer)?;
        if cancelled.load(Ordering::Relaxed) {
            break;
        }

        let enumeration_started = Instant::now();
        let read = match fs::read_dir(&directory) {
            Ok(read) => read,
            Err(error) => {
                warn(&mut writer, &mut profile, &mut counters, &directory, &format!("Dossier non accessible : {error}"))?;
                entries[parent_index].child_start = entries.len() as i32;
                entries[parent_index].child_count = 0;
                continue;
            }
        };
        let mut children = Vec::new();
        for child in read {
            match child {
                Ok(child) => children.push(child.path()),
                Err(error) => warn(&mut writer, &mut profile, &mut counters, &directory, &format!("Entrée ignorée : {error}"))?,
            }
        }
        profile.enumeration_ns += enumeration_started.elapsed().as_nanos();

        let sort_started = Instant::now();
        children.sort_unstable_by(|left, right| {
            left.file_name().cmp(&right.file_name()).then_with(|| left.cmp(right))
        });
        profile.sorting_ns += sort_started.elapsed().as_nanos();

        let child_start = entries.len();
        let mut child_directories = Vec::new();
        for child_path in children {
            let visited = counters.files + counters.directories;
            if visited.saturating_sub(last_cancel_poll) >= CANCEL_POLL_EVERY_ENTRIES {
                poll_cancel(&mut writer, &mut profile, &cancelled, &mut command_buffer)?;
                last_cancel_poll = visited;
                if cancelled.load(Ordering::Relaxed) {
                    break;
                }
            }

            profile.metadata_lookups += 1;
            let metadata_started = Instant::now();
            let child_metadata = match fs::symlink_metadata(&child_path) {
                Ok(metadata) => metadata,
                Err(error) => {
                    profile.metadata_ns += metadata_started.elapsed().as_nanos();
                    warn(&mut writer, &mut profile, &mut counters, &child_path, &format!("Métadonnées indisponibles : {error}"))?;
                    continue;
                }
            };
            profile.metadata_ns += metadata_started.elapsed().as_nanos();
            if child_metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
                warn(
                    &mut writer,
                    &mut profile,
                    &mut counters,
                    &child_path,
                    "Point de réanalyse ignoré pour éviter les cycles ou une sortie de la cible.",
                )?;
                continue;
            }

            if child_metadata.is_dir() {
                let index = entries.len();
                entries.push(Entry {
                    name: child_path.file_name().unwrap_or(child_path.as_os_str()).to_string_lossy().into_owned(),
                    parent_id: parent_index as i32,
                    child_start: 0,
                    child_count: 0,
                    is_directory: true,
                    logical_bytes: 0,
                    allocated_bytes: 0,
                    unique_logical_bytes: 0,
                    unique_allocated_bytes: 0,
                    hard_link_duplicate: false,
                });
                counters.directories += 1;
                child_directories.push((child_path, index));
            } else if child_metadata.is_file() {
                let logical_bytes = child_metadata.len();
                profile.file_information_lookups += 1;
                let file_information_started = Instant::now();
                let details = get_file_details(&child_path, logical_bytes);
                profile.file_information_ns += file_information_started.elapsed().as_nanos();
                let hard_link_duplicate = match details.identity {
                    Some(identity) => !hard_link_identities.insert(identity),
                    None => false,
                };
                if details.identity_expected && details.identity.is_none() {
                    warn(
                        &mut writer,
                        &mut profile,
                        &mut counters,
                        &child_path,
                        "Identité NTFS indisponible; ce fichier est compté sans déduplication des hardlinks.",
                    )?;
                }

                entries.push(Entry {
                    name: child_path.file_name().unwrap_or(child_path.as_os_str()).to_string_lossy().into_owned(),
                    parent_id: parent_index as i32,
                    child_start: 0,
                    child_count: 0,
                    is_directory: false,
                    logical_bytes,
                    allocated_bytes: details.allocation_size,
                    unique_logical_bytes: if hard_link_duplicate { 0 } else { logical_bytes },
                    unique_allocated_bytes: if hard_link_duplicate { 0 } else { details.allocation_size },
                    hard_link_duplicate,
                });
                counters.files += 1;
                if !hard_link_duplicate {
                    counters.unique_logical_bytes = counters.unique_logical_bytes.saturating_add(logical_bytes);
                }
            }

            let visited = counters.files + counters.directories;
            if visited.saturating_sub(last_progress) >= PROGRESS_EVERY_ENTRIES {
                send_progress(&mut writer, &mut profile, &counters, total_started.elapsed().as_millis() as u64)?;
                last_progress = visited;
            }
        }

        entries[parent_index].child_start = child_start as i32;
        entries[parent_index].child_count = (entries.len() - child_start) as i32;
        for child_directory in child_directories.into_iter().rev() {
            pending.push(child_directory);
        }
    }

    let aggregation_started = Instant::now();
    aggregate_unique_sizes(&mut entries);
    profile.aggregation_ns += aggregation_started.elapsed().as_nanos();

    send_progress(&mut writer, &mut profile, &counters, total_started.elapsed().as_millis() as u64)?;
    let results_complete = send_entries(&mut writer, &mut profile, &entries, &cancelled, &mut command_buffer)?;
    let state = if cancelled.load(Ordering::Relaxed) || !results_complete { "interrupted" } else { "completed" };
    let total_ms = total_started.elapsed().as_secs_f64() * 1000.0;
    let summary = serialize_summary(state, &counters, &entries[0], &profile, total_ms, "");
    send(&mut writer, &mut profile, &summary)?;
    Ok(())
}

fn parse_args() -> Result<(String, String), Box<dyn std::error::Error>> {
    let mut args = std::env::args().skip(1);
    let mut pipe_name = None;
    let mut target = None;
    while let Some(argument) = args.next() {
        match argument.as_str() {
            "--pipe-name" => pipe_name = args.next(),
            "--target" => target = args.next(),
            _ => return Err(format!("Argument inconnu : {argument}").into()),
        }
    }
    Ok((
        pipe_name.ok_or("Argument --pipe-name manquant")?,
        target.ok_or("Argument --target manquant")?,
    ))
}


fn get_file_details(path: &Path, logical_bytes: u64) -> FileDetails {
    let wide_path = win32_file_path(path);
    // SAFETY: the path is a valid NUL-terminated UTF-16 buffer and all Win32 pointers are valid for the calls.
    let handle = unsafe {
        CreateFileW(
            wide_path.as_ptr(),
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            std::ptr::null_mut(),
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            std::ptr::null_mut(),
        )
    };
    if handle as isize == -1 {
        return FileDetails { identity: None, allocation_size: logical_bytes, identity_expected: true };
    }

    let mut standard = FileStandardInfo::default();
    // SAFETY: the output buffer is initialized and its exact size is supplied.
    let has_standard = unsafe {
        GetFileInformationByHandleEx(
            handle,
            FILE_STANDARD_INFO_CLASS,
            (&mut standard as *mut FileStandardInfo).cast(),
            std::mem::size_of::<FileStandardInfo>() as u32,
        ) != 0
    };

    let mut legacy = ByHandleFileInformation::default();
    let legacy_needed = !has_standard;
    // Query the legacy information only when the standard class is unavailable, or later as an ID fallback.
    let has_legacy = if legacy_needed {
        // SAFETY: the output structure is correctly sized and initialized.
        unsafe { GetFileInformationByHandle(handle, &mut legacy) != 0 }
    } else {
        false
    };

    let link_count = if has_standard {
        Some(standard.number_of_links)
    } else if has_legacy {
        Some(legacy.number_of_links)
    } else {
        None
    };
    let identity_expected = link_count.is_none_or(|links| links > 1);
    let mut identity = None;
    if identity_expected {
        let mut file_id = FileIdInfo::default();
        // SAFETY: the output buffer is initialized and its exact size is supplied.
        let has_file_id = unsafe {
            GetFileInformationByHandleEx(
                handle,
                FILE_ID_INFO_CLASS,
                (&mut file_id as *mut FileIdInfo).cast(),
                std::mem::size_of::<FileIdInfo>() as u32,
            ) != 0
        };
        if has_file_id {
            identity = Some(FileIdentity(file_id.volume_serial_number, file_id.file_id));
        } else {
            if !has_legacy {
                // SAFETY: the output structure is correctly sized and initialized.
                let _ = unsafe { GetFileInformationByHandle(handle, &mut legacy) };
            }
            let legacy_id_available = legacy.volume_serial_number != 0 || legacy.number_of_links > 0;
            if legacy_id_available {
                let mut id = [0u8; 16];
                id[..4].copy_from_slice(&legacy.file_index_low.to_le_bytes());
                id[4..8].copy_from_slice(&legacy.file_index_high.to_le_bytes());
                identity = Some(FileIdentity(u64::from(legacy.volume_serial_number), id));
            }
        }
    }

    // SAFETY: handle was opened successfully above and is closed exactly once here.
    unsafe { CloseHandle(handle) };

    FileDetails {
        identity,
        allocation_size: if has_standard { standard.allocation_size.max(0) as u64 } else { logical_bytes },
        identity_expected,
    }
}

fn win32_file_path(path: &Path) -> Vec<u16> {
    let path_units: Vec<u16> = path.as_os_str().encode_wide().collect();
    let slash = '\\' as u16;
    let starts_with = |prefix: &[u16]| path_units.starts_with(prefix);
    let already_extended = starts_with(&[slash, slash, '?' as u16, slash]) || starts_with(&[slash, slash, '.' as u16, slash]);

    let mut extended = Vec::with_capacity(path_units.len() + 8);
    if path_units.len() + 1 > MAX_PATH_UNITS && !already_extended {
        if starts_with(&[slash, slash]) {
            // Win32 extended UNC paths use \\?\UNC\ followed by the server/share portion.
            extended.extend("\\\\?\\UNC\\".encode_utf16());
            extended.extend_from_slice(&path_units[2..]);
        } else if path_units.len() >= 3 && path_units[1] == ':' as u16 && path_units[2] == slash {
            // The scanner passes absolute drive paths after Path::GetFullPath.
            extended.extend("\\\\?\\".encode_utf16());
            extended.extend_from_slice(&path_units);
        } else {
            extended.extend_from_slice(&path_units);
        }
    } else {
        extended.extend_from_slice(&path_units);
    }
    extended.push(0);
    extended
}

#[cfg(test)]
mod tests {
    use super::win32_file_path;
    use std::path::Path;

    #[test]
    fn long_absolute_drive_path_gets_extended_length_prefix() {
        let path = format!("C:\\{}", "x".repeat(270));
        let encoded = win32_file_path(Path::new(&path));
        assert!(encoded.starts_with(&"\\\\?\\C:\\".encode_utf16().collect::<Vec<_>>()));
        assert_eq!(encoded.last(), Some(&0));
    }

    #[test]
    fn long_unc_path_gets_extended_unc_prefix() {
        let path = format!("\\\\server\\share\\{}", "x".repeat(270));
        let encoded = win32_file_path(Path::new(&path));
        assert!(encoded.starts_with(&"\\\\?\\UNC\\server\\share\\".encode_utf16().collect::<Vec<_>>()));
        assert_eq!(encoded.last(), Some(&0));
    }
}
