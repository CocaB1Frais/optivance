//! Accès Windows du helper MFT : validation des arguments, ouverture du volume en
//! lecture seule, appel `FSCTL_ENUM_USN_DATA` et lecture des tailles par `OpenFileById`.
//!
//! Aucune écriture : ni sur le volume, ni sur le journal USN (jamais créé, modifié ni lu ;
//! seule l'énumération MFT est appelée). Aucun flux de données de fichier n'est ouvert.

use std::ffi::c_void;
use std::io;
use std::os::windows::io::{AsRawHandle, FromRawHandle, OwnedHandle};
use std::path::Path;

use super::assemble::FileSizes;
use super::metadata::{FileInfo, REPARSE_TAG_NAME_SURROGATE};

/// Préfixe imposé au nom de pipe : le helper élevé refuse tout autre nom, pour qu'aucun
/// argument ne puisse le faire écrire ailleurs que dans le canal de l'application.
pub const PIPE_PREFIX: &str = "optivance-mft-";

/// Accepte uniquement « X » ou « X: » (lettre ASCII), renvoyée en majuscule.
pub fn parse_volume_letter(argument: &str) -> Option<char> {
    let mut characters = argument.chars();
    let letter = characters.next().filter(char::is_ascii_alphabetic)?;
    match (characters.next(), characters.next()) {
        (None, None) | (Some(':'), None) => Some(letter.to_ascii_uppercase()),
        _ => None,
    }
}

/// `optivance-mft-` suivi de 32 chiffres hexadécimaux minuscules (Guid « N » du client).
pub fn is_valid_pipe_name(name: &str) -> bool {
    name.strip_prefix(PIPE_PREFIX)
        .is_some_and(|suffix| suffix.len() == 32 && suffix.bytes().all(|byte| matches!(byte, b'0'..=b'9' | b'a'..=b'f')))
}

/// À appeler en tout premier dans le helper élevé : les DLL chargées dynamiquement
/// (ex. dbghelp) ne sont plus cherchées dans le dossier de l'application.
pub fn restrict_dll_search_to_system32() -> io::Result<()> {
    // SAFETY: appel sans pointeur ; le drapeau est une valeur documentée.
    if unsafe { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32) } == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(())
}

pub struct RootDirectory {
    handle: OwnedHandle,
    /// Référence NTFS 64 bits du dossier racine.
    pub file_id: u64,
}

pub fn filesystem_name(letter: char) -> io::Result<String> {
    let root = wide(&format!("{letter}:\\"));
    let mut name = [0u16; 64];
    // SAFETY: root est terminé par NUL ; le buffer de nom et sa taille sont exacts ; les sorties facultatives sont nulles.
    let ok = unsafe {
        GetVolumeInformationW(
            root.as_ptr(),
            std::ptr::null_mut(),
            0,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            name.as_mut_ptr(),
            name.len() as u32,
        )
    };
    if ok == 0 {
        return Err(io::Error::last_os_error());
    }
    let length = name.iter().position(|unit| *unit == 0).unwrap_or(name.len());
    Ok(String::from_utf16_lossy(&name[..length]))
}

pub fn open_root(letter: char) -> io::Result<RootDirectory> {
    let handle = open_for_attributes(Path::new(&format!("{letter}:\\")))?;
    let file_id = file_index(&handle)?;
    Ok(RootDirectory { handle, file_id })
}

/// Exige les droits administrateur (refus attendu : erreur 5 sans élévation).
pub fn open_volume(letter: char) -> io::Result<OwnedHandle> {
    // Lecture seule : GENERIC_READ uniquement, aucun droit d'écriture demandé.
    open_handle(&wide(&format!("\\\\.\\{letter}:")), GENERIC_READ, 0)
}

/// Lecture exacte à une position du volume (offset et longueur alignés sur le secteur).
pub fn read_volume_at(volume: &std::fs::File, offset: u64, buffer: &mut [u8]) -> Result<(), String> {
    use std::os::windows::fs::FileExt;
    let mut filled = 0;
    while filled < buffer.len() {
        match volume.seek_read(&mut buffer[filled..], offset + filled as u64) {
            Ok(0) => return Err(format!("fin du volume à l'offset {}", offset + filled as u64)),
            Ok(read) => filled += read,
            Err(error) => return Err(error.to_string()),
        }
    }
    Ok(())
}

/// Un appel `FSCTL_ENUM_USN_DATA` ; renvoie les octets écrits ou le code Win32.
pub fn fetch_mft_chunk(volume: &impl AsRawHandle, start: u64, buffer: &mut [u8]) -> Result<usize, i32> {
    let mut input = MftEnumDataV0 { start_file_reference_number: start, low_usn: 0, high_usn: i64::MAX };
    let mut returned = 0u32;
    let capacity = u32::try_from(buffer.len()).unwrap_or(u32::MAX);
    // SAFETY: entrée et sortie sont des buffers valides de la taille indiquée ; appel synchrone sans OVERLAPPED.
    let ok = unsafe {
        DeviceIoControl(
            volume.as_raw_handle(),
            FSCTL_ENUM_USN_DATA,
            (&mut input as *mut MftEnumDataV0).cast(),
            std::mem::size_of::<MftEnumDataV0>() as u32,
            buffer.as_mut_ptr().cast(),
            capacity,
            &mut returned,
            std::ptr::null_mut(),
        )
    };
    if ok == 0 {
        return Err(io::Error::last_os_error().raw_os_error().unwrap_or(-1));
    }
    Ok(returned as usize)
}

pub fn query_sizes(root: &RootDirectory, file_id: u128) -> io::Result<FileSizes> {
    Ok(query_file(root, file_id, false)?.sizes)
}

/// Métadonnées d'une référence. Avec `want_reparse_tag`, le point de réanalyse est
/// d'abord ouvert lui-même (sans le suivre) pour lire son tag ; s'il ne s'agit pas d'un
/// lien (name surrogate), les tailles sont relues à travers son pilote, comme le voit le
/// scan classique. `FILE_FLAG_OPEN_NO_RECALL` : aucun rappel/téléchargement de données.
pub fn query_file(root: &RootDirectory, file_id: u128, want_reparse_tag: bool) -> io::Result<FileInfo> {
    if !want_reparse_tag {
        let handle = open_by_id(root, file_id, FILE_FLAG_OPEN_NO_RECALL)?;
        return Ok(FileInfo { sizes: standard_info(&handle)?, reparse_tag: None });
    }
    let raw_view = open_by_id(root, file_id, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_OPEN_NO_RECALL)?;
    let tag = reparse_tag(&raw_view)?;
    let filtered_view = if tag != 0 && tag & REPARSE_TAG_NAME_SURROGATE == 0 {
        open_by_id(root, file_id, FILE_FLAG_OPEN_NO_RECALL).ok()
    } else {
        None
    };
    let sizes = standard_info(filtered_view.as_ref().unwrap_or(&raw_view))?;
    Ok(FileInfo { sizes, reparse_tag: Some(tag) })
}

/// Tous les noms d'un fichier, relatifs à la racine du volume (« \dossier\nom »).
pub fn hard_link_names(letter: char, volume_relative_path: &str) -> io::Result<Vec<String>> {
    let path = wide(&format!("\\\\?\\{letter}:{volume_relative_path}"));
    let mut buffer = vec![0u16; 512];
    let mut raw = INVALID_HANDLE;
    for _ in 0..2 {
        let mut length = buffer.len() as u32;
        // SAFETY: path est terminé par NUL ; buffer contient `length` caractères inscriptibles.
        raw = unsafe { FindFirstFileNameW(path.as_ptr(), 0, &mut length, buffer.as_mut_ptr()) };
        if raw != INVALID_HANDLE {
            break;
        }
        let error = io::Error::last_os_error();
        if error.raw_os_error() != Some(ERROR_MORE_DATA) {
            return Err(error);
        }
        buffer.resize(length as usize + 1, 0);
    }
    if raw == INVALID_HANDLE {
        return Err(io::Error::last_os_error());
    }
    let search = FindNameHandle(raw);
    let mut names = vec![until_nul(&buffer)];
    loop {
        let mut length = buffer.len() as u32;
        // SAFETY: handle de recherche valide ; buffer contient `length` caractères inscriptibles.
        if unsafe { FindNextFileNameW(search.0, &mut length, buffer.as_mut_ptr()) } != 0 {
            names.push(until_nul(&buffer));
            continue;
        }
        let error = io::Error::last_os_error();
        match error.raw_os_error() {
            Some(ERROR_HANDLE_EOF) => return Ok(names),
            Some(ERROR_MORE_DATA) => buffer.resize(length as usize + 1, 0),
            _ => return Err(error),
        }
    }
}

/// Handle de `FindFirstFileNameW`, fermé par `FindClose` (et non `CloseHandle`).
struct FindNameHandle(Handle);

impl Drop for FindNameHandle {
    fn drop(&mut self) {
        // SAFETY: handle de recherche valide, fermé une seule fois.
        unsafe { FindClose(self.0) };
    }
}

fn until_nul(units: &[u16]) -> String {
    let length = units.iter().position(|unit| *unit == 0).unwrap_or(units.len());
    String::from_utf16_lossy(&units[..length])
}

fn open_by_id(root: &RootDirectory, file_id: u128, extra_flags: u32) -> io::Result<OwnedHandle> {
    let descriptor = match u64::try_from(file_id) {
        Ok(id) => FileIdDescriptor { size: 24, kind: FILE_ID_TYPE, file_id: [id, 0] },
        Err(_) => FileIdDescriptor { size: 24, kind: EXTENDED_FILE_ID_TYPE, file_id: [file_id as u64, (file_id >> 64) as u64] },
    };
    // SAFETY: le descripteur est initialisé et dimensionné ; root.handle est un handle valide du volume.
    let raw = unsafe {
        OpenFileById(
            root.handle.as_raw_handle(),
            &descriptor,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            std::ptr::null_mut(),
            FILE_FLAG_BACKUP_SEMANTICS | extra_flags,
        )
    };
    owned(raw)
}

fn standard_info(handle: &OwnedHandle) -> io::Result<FileSizes> {
    let mut standard = FileStandardInfo::default();
    // SAFETY: la structure de sortie est initialisée et sa taille exacte est fournie.
    let ok = unsafe {
        GetFileInformationByHandleEx(
            handle.as_raw_handle(),
            FILE_STANDARD_INFO_CLASS,
            (&mut standard as *mut FileStandardInfo).cast(),
            std::mem::size_of::<FileStandardInfo>() as u32,
        )
    };
    if ok == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(FileSizes {
        logical: standard.end_of_file.max(0) as u64,
        allocated: standard.allocation_size.max(0) as u64,
        links: standard.number_of_links,
    })
}

fn reparse_tag(handle: &OwnedHandle) -> io::Result<u32> {
    let mut info = FileAttributeTagInfo::default();
    // SAFETY: la structure de sortie est initialisée et sa taille exacte est fournie.
    let ok = unsafe {
        GetFileInformationByHandleEx(
            handle.as_raw_handle(),
            FILE_ATTRIBUTE_TAG_INFO_CLASS,
            (&mut info as *mut FileAttributeTagInfo).cast(),
            std::mem::size_of::<FileAttributeTagInfo>() as u32,
        )
    };
    if ok == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(info.reparse_tag)
}

/// Référence NTFS d'un chemin existant (sert à la racine et aux tests).
pub fn file_id_of(path: &Path) -> io::Result<u64> {
    file_index(&open_for_attributes(path)?)
}

fn open_for_attributes(path: &Path) -> io::Result<OwnedHandle> {
    use std::os::windows::ffi::OsStrExt;
    let mut units: Vec<u16> = path.as_os_str().encode_wide().collect();
    units.push(0);
    open_handle(&units, FILE_READ_ATTRIBUTES, FILE_FLAG_BACKUP_SEMANTICS)
}

fn open_handle(path: &[u16], access: u32, flags: u32) -> io::Result<OwnedHandle> {
    // SAFETY: path est terminé par NUL ; les pointeurs facultatifs sont nuls.
    let raw = unsafe {
        CreateFileW(
            path.as_ptr(),
            access,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            std::ptr::null_mut(),
            OPEN_EXISTING,
            flags,
            std::ptr::null_mut(),
        )
    };
    owned(raw)
}

fn owned(raw: Handle) -> io::Result<OwnedHandle> {
    if raw.is_null() || raw as isize == -1 {
        return Err(io::Error::last_os_error());
    }
    // SAFETY: handle valide venant d'être ouvert ; OwnedHandle le fermera une seule fois.
    Ok(unsafe { OwnedHandle::from_raw_handle(raw) })
}

fn file_index(handle: &OwnedHandle) -> io::Result<u64> {
    let mut info = ByHandleFileInformation::default();
    // SAFETY: la structure de sortie est initialisée et correctement dimensionnée.
    if unsafe { GetFileInformationByHandle(handle.as_raw_handle(), &mut info) } == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok((u64::from(info.file_index_high) << 32) | u64::from(info.file_index_low))
}

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

type Handle = *mut c_void;

const GENERIC_READ: u32 = 0x8000_0000;
const FILE_READ_ATTRIBUTES: u32 = 0x0080;
const FILE_SHARE_READ: u32 = 0x0000_0001;
const FILE_SHARE_WRITE: u32 = 0x0000_0002;
const FILE_SHARE_DELETE: u32 = 0x0000_0004;
const OPEN_EXISTING: u32 = 3;
const FILE_FLAG_BACKUP_SEMANTICS: u32 = 0x0200_0000;
const FILE_FLAG_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
const FILE_FLAG_OPEN_NO_RECALL: u32 = 0x0010_0000;
const FILE_STANDARD_INFO_CLASS: i32 = 1;
const FILE_ATTRIBUTE_TAG_INFO_CLASS: i32 = 9;
const ERROR_HANDLE_EOF: i32 = 38;
const ERROR_MORE_DATA: i32 = 234;
const INVALID_HANDLE: Handle = -1isize as Handle;
const FILE_ID_TYPE: i32 = 0;
const EXTENDED_FILE_ID_TYPE: i32 = 2;
/// CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 44, METHOD_NEITHER, FILE_ANY_ACCESS).
const FSCTL_ENUM_USN_DATA: u32 = 0x0009_00B3;

#[repr(C)]
struct MftEnumDataV0 {
    start_file_reference_number: u64,
    low_usn: i64,
    high_usn: i64,
}

#[repr(C)]
struct FileIdDescriptor {
    size: u32,
    kind: i32,
    /// Union LARGE_INTEGER / FILE_ID_128 (alignée sur 8 octets, offset 8).
    file_id: [u64; 2],
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
struct FileAttributeTagInfo {
    file_attributes: u32,
    reparse_tag: u32,
}

#[repr(C)]
#[derive(Default)]
struct ByHandleFileInformation {
    file_attributes: u32,
    creation_time: [u32; 2],
    last_access_time: [u32; 2],
    last_write_time: [u32; 2],
    volume_serial_number: u32,
    file_size_high: u32,
    file_size_low: u32,
    number_of_links: u32,
    file_index_high: u32,
    file_index_low: u32,
}

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
    fn OpenFileById(
        volume_hint: Handle,
        file_id: *const FileIdDescriptor,
        desired_access: u32,
        share_mode: u32,
        security_attributes: *mut c_void,
        flags_and_attributes: u32,
    ) -> Handle;
    fn DeviceIoControl(
        device: Handle,
        control_code: u32,
        in_buffer: *mut c_void,
        in_buffer_size: u32,
        out_buffer: *mut c_void,
        out_buffer_size: u32,
        bytes_returned: *mut u32,
        overlapped: *mut c_void,
    ) -> i32;
    fn GetVolumeInformationW(
        root_path_name: *const u16,
        volume_name_buffer: *mut u16,
        volume_name_size: u32,
        volume_serial_number: *mut u32,
        maximum_component_length: *mut u32,
        file_system_flags: *mut u32,
        file_system_name_buffer: *mut u16,
        file_system_name_size: u32,
    ) -> i32;
    fn GetFileInformationByHandle(file: Handle, info: *mut ByHandleFileInformation) -> i32;
    fn GetFileInformationByHandleEx(file: Handle, class: i32, info: *mut c_void, size: u32) -> i32;
    fn SetDefaultDllDirectories(directory_flags: u32) -> i32;
    fn FindFirstFileNameW(file_name: *const u16, flags: u32, string_length: *mut u32, link_name: *mut u16) -> Handle;
    fn FindNextFileNameW(find_stream: Handle, string_length: *mut u32, link_name: *mut u16) -> i32;
    fn FindClose(find_file: Handle) -> i32;
}

const LOAD_LIBRARY_SEARCH_SYSTEM32: u32 = 0x0000_0800;

const _: () = assert!(std::mem::size_of::<FileIdDescriptor>() == 24);
const _: () = assert!(std::mem::size_of::<MftEnumDataV0>() == 24);
const _: () = assert!(std::mem::size_of::<FileStandardInfo>() == 24);
const _: () = assert!(std::mem::size_of::<ByHandleFileInformation>() == 52);

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn restricts_dynamic_dll_search_to_system32() {
        restrict_dll_search_to_system32().unwrap();
    }

    #[test]
    fn accepts_only_a_single_drive_letter() {
        assert_eq!(parse_volume_letter("C"), Some('C'));
        assert_eq!(parse_volume_letter("d:"), Some('D'));
        for rejected in ["", "C:\\", "CD", "1", "é", "C:x", "\\\\.\\C:", "C: "] {
            assert_eq!(parse_volume_letter(rejected), None, "{rejected:?}");
        }
    }

    #[test]
    fn accepts_only_application_pipe_names() {
        assert!(is_valid_pipe_name("optivance-mft-0123456789abcdef0123456789abcdef"));
        for rejected in [
            "",
            "optivance-mft-",
            "optivance-mft-0123456789ABCDEF0123456789ABCDEF",
            "optivance-mft-0123456789abcdef0123456789abcde",
            "optivance-mft-0123456789abcdef0123456789abcdef0",
            "optivance-scan-v2-0123456789abcdef0123456789abcdef",
            "..\\..\\C:\\Windows\\x",
            "optivance-mft-..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..",
        ] {
            assert!(!is_valid_pipe_name(rejected), "{rejected:?}");
        }
    }

    /// Dossier temporaire supprimé à la fin du test, même en cas d'échec d'assertion.
    struct Fixture(std::path::PathBuf);

    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn fixture() -> Fixture {
        let nanos = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let path = std::env::temp_dir().join(format!("optivance-mft-test-{}-{nanos}", std::process::id()));
        fs::create_dir(&path).unwrap();
        Fixture(path)
    }

    fn temp_letter() -> char {
        std::env::temp_dir().to_string_lossy().chars().next().unwrap()
    }

    #[test]
    fn reads_filesystem_name_of_temp_volume() {
        assert!(!filesystem_name(temp_letter()).unwrap().is_empty());
    }

    #[test]
    fn root_directory_reference_matches_file_id_of_root_path() {
        let letter = temp_letter();
        let root = open_root(letter).unwrap();
        assert_eq!(root.file_id, file_id_of(Path::new(&format!("{letter}:\\"))).unwrap());
    }

    #[test]
    fn reads_sizes_and_link_count_by_file_reference() {
        let fixture = fixture();
        let original = fixture.0.join("original.bin");
        fs::write(&original, vec![7u8; 5000]).unwrap();
        fs::hard_link(&original, fixture.0.join("lien.bin")).unwrap();
        let id = file_id_of(&original).unwrap();
        assert_eq!(id, file_id_of(&fixture.0.join("lien.bin")).unwrap());

        let sizes = query_sizes(&open_root(temp_letter()).unwrap(), u128::from(id)).unwrap();
        assert_eq!((sizes.logical, sizes.links), (5000, 2));
        assert!(sizes.allocated >= 5000, "allocation {}", sizes.allocated);
    }

    #[test]
    fn lists_every_hard_link_name_relative_to_volume_root() {
        let fixture = fixture();
        let original = fixture.0.join("original.bin");
        fs::write(&original, b"x").unwrap();
        fs::create_dir(fixture.0.join("sous")).unwrap();
        fs::hard_link(&original, fixture.0.join("sous").join("alias.bin")).unwrap();
        let relative = original.to_string_lossy()[2..].to_owned();
        let mut names: Vec<String> = hard_link_names(temp_letter(), &relative).unwrap().into_iter().map(|n| n.to_lowercase()).collect();
        names.sort();
        let directory = fixture.0.to_string_lossy()[2..].to_lowercase();
        assert_eq!(names, [format!("{directory}\\original.bin"), format!("{directory}\\sous\\alias.bin")]);
    }

    #[test]
    fn query_file_reads_reparse_tag_only_when_requested() {
        let fixture = fixture();
        let file = fixture.0.join("f.bin");
        fs::write(&file, vec![1u8; 10]).unwrap();
        let root = open_root(temp_letter()).unwrap();
        let id = u128::from(file_id_of(&file).unwrap());
        assert_eq!(query_file(&root, id, false).unwrap().reparse_tag, None);
        let with_tag = query_file(&root, id, true).unwrap();
        assert_eq!((with_tag.reparse_tag, with_tag.sizes.logical), (Some(0), 10));
    }

    #[test]
    fn unknown_file_reference_is_an_error_not_a_zero_size() {
        let root = open_root(temp_letter()).unwrap();
        assert!(query_sizes(&root, u128::from(0x0000_00FF_FFFF_FFF0u64)).is_err());
    }
}
