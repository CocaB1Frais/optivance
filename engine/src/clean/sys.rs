//! Appels Windows du nettoyage approfondi : dossier Windows réel (jamais une variable
//! d'environnement, modifiable par un processus non élevé) et suppression d'un fichier par
//! handle après vérification de son chemin final (aucune jonction ne peut rediriger la
//! suppression hors de l'emplacement du catalogue).

use std::ffi::c_void;
use std::fs::OpenOptions;
use std::io;
use std::os::windows::fs::OpenOptionsExt;
use std::os::windows::io::AsRawHandle;
use std::path::Path;

const DELETE: u32 = 0x0001_0000;
const FILE_READ_ATTRIBUTES: u32 = 0x0080;
const FILE_SHARE_ALL: u32 = 0x1 | 0x2 | 0x4;
const FILE_FLAG_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
const FILE_ATTRIBUTE_DIRECTORY: u32 = 0x10;
const FILE_DISPOSITION_INFO: i32 = 4;
const FILE_DISPOSITION_INFO_EX: i32 = 21;
const FILE_DISPOSITION_FLAG_DELETE: u32 = 0x1;
const FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE: u32 = 0x10;

/// Dossier Windows du système (ex. `C:\Windows`), indépendant de l'environnement.
pub fn system_windows_directory() -> io::Result<String> {
    let mut buffer = [0u16; 520];
    // SAFETY: tampon valide de la taille indiquée.
    let length = unsafe { GetSystemWindowsDirectoryW(buffer.as_mut_ptr(), buffer.len() as u32) } as usize;
    if length == 0 || length >= buffer.len() {
        return Err(io::Error::last_os_error());
    }
    Ok(String::from_utf16_lossy(&buffer[..length]))
}

/// Chemin final (jonctions résolues) au format `\\?\C:\...`, en majuscules pour comparaison.
fn final_path(handle: *mut c_void) -> io::Result<String> {
    let mut buffer = vec![0u16; 1024];
    loop {
        // SAFETY: handle ouvert, tampon valide de la taille indiquée.
        let length = unsafe { GetFinalPathNameByHandleW(handle, buffer.as_mut_ptr(), buffer.len() as u32, 0) } as usize;
        if length == 0 {
            return Err(io::Error::last_os_error());
        }
        if length < buffer.len() {
            return Ok(String::from_utf16_lossy(&buffer[..length]).to_uppercase());
        }
        buffer.resize(length + 1, 0);
    }
}

/// Chemin final d'un dossier (pour fixer la racine autorisée).
pub fn canonical_folder(path: &Path) -> io::Result<String> {
    let folder = OpenOptions::new()
        .access_mode(FILE_READ_ATTRIBUTES)
        .share_mode(FILE_SHARE_ALL)
        .custom_flags(0x0200_0000 /* FILE_FLAG_BACKUP_SEMANTICS */)
        .open(path)?;
    final_path(folder.as_raw_handle())
}

/// Résultat d'une tentative de suppression.
#[derive(Debug, PartialEq, Eq)]
pub enum Removal {
    Deleted,
    /// Hors de l'emplacement autorisé, lien, dossier : refusé sans rien toucher.
    Refused,
    /// En cours d'utilisation ou protégé.
    Failed,
}

/// Supprime `path` uniquement si, une fois ouvert (sans suivre de point d'analyse), son chemin
/// final commence par `root` (chemin final du dossier autorisé, suivi d'un `\`), qu'il ne s'agit
/// ni d'un lien ni d'un dossier. La suppression porte sur le handle vérifié.
pub fn delete_file_within(path: &Path, root: &str) -> Removal {
    let Ok(file) = OpenOptions::new()
        .access_mode(DELETE | FILE_READ_ATTRIBUTES)
        .share_mode(FILE_SHARE_ALL)
        // Dossiers ouverts aussi (pour être refusés explicitement), sans suivre de point d'analyse.
        .custom_flags(FILE_FLAG_OPEN_REPARSE_POINT | 0x0200_0000 /* FILE_FLAG_BACKUP_SEMANTICS */)
        .open(path)
    else {
        return Removal::Failed;
    };
    let Ok(metadata) = file.metadata() else {
        return Removal::Failed;
    };
    use std::os::windows::fs::MetadataExt;
    if metadata.file_attributes() & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY) != 0 {
        return Removal::Refused;
    }
    let handle = file.as_raw_handle();
    match final_path(handle) {
        Ok(final_path) if final_path.starts_with(root) && final_path.len() > root.len() => {}
        _ => return Removal::Refused,
    }
    let extended = [FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE];
    // SAFETY: handle ouvert avec DELETE ; structures de la taille indiquée.
    let done = unsafe { SetFileInformationByHandle(handle, FILE_DISPOSITION_INFO_EX, extended.as_ptr().cast(), 4) } != 0 || {
        let classic = [1u8];
        unsafe { SetFileInformationByHandle(handle, FILE_DISPOSITION_INFO, classic.as_ptr().cast(), 1) != 0 }
    };
    if done { Removal::Deleted } else { Removal::Failed }
}

#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetSystemWindowsDirectoryW(buffer: *mut u16, size: u32) -> u32;
    fn GetFinalPathNameByHandleW(file: *mut c_void, buffer: *mut u16, size: u32, flags: u32) -> u32;
    fn SetFileInformationByHandle(file: *mut c_void, class: i32, information: *const c_void, size: u32) -> i32;
}
