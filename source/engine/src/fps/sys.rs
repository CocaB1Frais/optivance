//! Session ETW temps réel des événements « Present » (DXGI, D3D9). Structures Windows
//! écrites octet par octet aux décalages documentés (x64) pour ne dépendre d'aucune crate.

use std::ffi::c_void;
use std::sync::{Mutex, OnceLock};
use std::thread::JoinHandle;

use super::{FpsCounter, Source};

type TraceHandle = u64;
const INVALID_PROCESSTRACE_HANDLE: TraceHandle = u64::MAX;

const SESSION_NAME: &str = "OPTIVANCE-FPS";
/// Microsoft-Windows-DXGI : Present_Start = 42.
const DXGI: [u8; 16] = guid(0xCA11C036, 0x0102, 0x4A2D, [0xA6, 0xAD, 0xF0, 0x3C, 0xFE, 0xD5, 0xD3, 0xC9]);
const DXGI_PRESENT_START: u16 = 42;
/// Microsoft-Windows-D3D9 : Present_Start = 1.
const D3D9: [u8; 16] = guid(0x783ACA0A, 0x790E, 0x4D7F, [0x84, 0x51, 0xAA, 0x85, 0x05, 0x11, 0xC6, 0xB9]);
const D3D9_PRESENT_START: u16 = 1;
/// Microsoft-Windows-DxgKrnl (identifiants de PresentMon, licence MIT) : Present_Info = 184,
/// PresentHistory_Start = 171, PresentHistoryDetailed_Start = 215 ; mot-clé « Present ».
const DXGKRNL: [u8; 16] = guid(0x802EC45A, 0x1E99, 0x4B83, [0x99, 0x20, 0x87, 0xC9, 0x82, 0x77, 0xBA, 0x9D]);
const DXGKRNL_PRESENT_INFO: u16 = 184;
const DXGKRNL_PRESENT_HISTORY_START: u16 = 171;
const DXGKRNL_PRESENT_HISTORY_DETAILED_START: u16 = 215;
const DXGKRNL_KEYWORD_PRESENT: u64 = 0x0800_0000;

const PROPERTIES_SIZE: usize = 120;
const LOGFILE_SIZE: usize = 448;
const WNODE_FLAG_TRACED_GUID: u32 = 0x0002_0000;
const EVENT_TRACE_REAL_TIME_MODE: u32 = 0x0000_0100;
const EVENT_TRACE_CONTROL_STOP: u32 = 1;
const EVENT_CONTROL_CODE_ENABLE_PROVIDER: u32 = 1;
const TRACE_LEVEL_INFORMATION: u8 = 4;
const PROCESS_TRACE_MODE_REAL_TIME: u32 = 0x0000_0100;
const PROCESS_TRACE_MODE_EVENT_RECORD: u32 = 0x1000_0000;
const ERROR_ALREADY_EXISTS: u32 = 183;

const fn guid(data1: u32, data2: u16, data3: u16, data4: [u8; 8]) -> [u8; 16] {
    let a = data1.to_le_bytes();
    let b = data2.to_le_bytes();
    let c = data3.to_le_bytes();
    [a[0], a[1], a[2], a[3], b[0], b[1], c[0], c[1], data4[0], data4[1], data4[2], data4[3], data4[4], data4[5], data4[6], data4[7]]
}

fn counter() -> &'static Mutex<FpsCounter> {
    static COUNTER: OnceLock<Mutex<FpsCounter>> = OnceLock::new();
    COUNTER.get_or_init(|| Mutex::new(FpsCounter::default()))
}

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// EVENT_TRACE_PROPERTIES + nom de session, dans un tampon aligné sur 8 octets.
fn properties() -> Vec<u64> {
    let name = wide(SESSION_NAME);
    let total = PROPERTIES_SIZE + name.len() * 2;
    let mut buffer = vec![0u64; total.div_ceil(8)];
    // SAFETY: tampon de `total` octets au moins, aligné sur 8.
    let bytes = unsafe { std::slice::from_raw_parts_mut(buffer.as_mut_ptr().cast::<u8>(), total) };
    let put = |bytes: &mut [u8], offset: usize, value: u32| bytes[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
    put(bytes, 0, total as u32); // Wnode.BufferSize
    put(bytes, 40, 1); // Wnode.ClientContext = QPC
    put(bytes, 44, WNODE_FLAG_TRACED_GUID); // Wnode.Flags
    put(bytes, 48, 64); // BufferSize (Ko)
    put(bytes, 52, 4); // MinimumBuffers
    put(bytes, 56, 16); // MaximumBuffers
    put(bytes, 64, EVENT_TRACE_REAL_TIME_MODE); // LogFileMode
    put(bytes, 68, 1); // FlushTimer (s)
    put(bytes, 116, PROPERTIES_SIZE as u32); // LoggerNameOffset
    buffer
}

fn qpc() -> (i64, i64) {
    let (mut now, mut frequency) = (0i64, 0i64);
    // SAFETY: pointeurs vers des entiers locaux.
    unsafe {
        QueryPerformanceCounter(&mut now);
        QueryPerformanceFrequency(&mut frequency);
    }
    (now, frequency)
}

/// FPS actuels de `pid` (None si rien n'est affiché).
pub fn query(pid: u32) -> Option<f64> {
    let (now, frequency) = qpc();
    let mut counter = counter().lock().ok()?;
    counter.prune(now, frequency);
    counter.fps(pid, now, frequency)
}

unsafe extern "system" fn on_event(record: *mut c_void) {
    if record.is_null() {
        return;
    }
    let base = record.cast::<u8>();
    // EVENT_RECORD.EventHeader : ProcessId @12, TimeStamp @16, ProviderId @24, EventDescriptor.Id @40.
    // SAFETY: ETW fournit un EVENT_RECORD valide pendant l'appel.
    let (pid, timestamp, provider, id) = unsafe {
        (
            std::ptr::read_unaligned(base.add(12).cast::<u32>()),
            std::ptr::read_unaligned(base.add(16).cast::<i64>()),
            std::ptr::read_unaligned(base.add(24).cast::<[u8; 16]>()),
            std::ptr::read_unaligned(base.add(40).cast::<u16>()),
        )
    };
    let source = if (provider == DXGI && id == DXGI_PRESENT_START) || (provider == D3D9 && id == D3D9_PRESENT_START) {
        Source::Runtime
    } else if provider == DXGKRNL && id == DXGKRNL_PRESENT_INFO {
        Source::KernelPresent
    } else if provider == DXGKRNL && (id == DXGKRNL_PRESENT_HISTORY_START || id == DXGKRNL_PRESENT_HISTORY_DETAILED_START) {
        Source::KernelHistory
    } else {
        return;
    };
    if let Ok(mut counter) = counter().lock() {
        counter.record_from(pid, source, timestamp);
    }
}

/// Session ETW en cours ; arrêtée par `stop` (ou à la destruction).
pub struct FpsSession {
    session: TraceHandle,
    trace: TraceHandle,
    worker: Option<JoinHandle<()>>,
}

fn stop_named() {
    let name = wide(SESSION_NAME);
    let mut props = properties();
    // SAFETY: nom et tampon valides ; arrêt d'une éventuelle session orpheline du même nom.
    unsafe { ControlTraceW(0, name.as_ptr(), props.as_mut_ptr().cast(), EVENT_TRACE_CONTROL_STOP) };
}

impl FpsSession {
    pub fn start() -> Result<Self, u32> {
        let name = wide(SESSION_NAME);
        let mut props = properties();
        let mut session: TraceHandle = 0;
        // SAFETY: tampons valides de la taille déclarée.
        let mut status = unsafe { StartTraceW(&mut session, name.as_ptr(), props.as_mut_ptr().cast()) };
        if status == ERROR_ALREADY_EXISTS {
            stop_named();
            props = properties();
            // SAFETY: idem.
            status = unsafe { StartTraceW(&mut session, name.as_ptr(), props.as_mut_ptr().cast()) };
        }
        if status != 0 {
            return Err(status);
        }
        for (provider, keyword) in [(DXGI, 0), (D3D9, 0), (DXGKRNL, DXGKRNL_KEYWORD_PRESENT)] {
            // SAFETY: session ouverte, GUID valide.
            unsafe { EnableTraceEx2(session, provider.as_ptr().cast(), EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, keyword, 0, 0, std::ptr::null_mut()) };
        }

        let mut logfile = vec![0u64; LOGFILE_SIZE / 8];
        let raw = logfile.as_mut_ptr().cast::<u8>();
        // EVENT_TRACE_LOGFILEW : LoggerName @8, ProcessTraceMode @28, EventRecordCallback @424.
        // SAFETY: tampon de LOGFILE_SIZE octets ; `name` vit jusqu'à la fin d'OpenTraceW.
        let trace = unsafe {
            std::ptr::write_unaligned(raw.add(8).cast::<*const u16>(), name.as_ptr());
            std::ptr::write_unaligned(raw.add(28).cast::<u32>(), PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD);
            std::ptr::write_unaligned(raw.add(424).cast::<usize>(), on_event as *const () as usize);
            OpenTraceW(raw.cast())
        };
        if trace == INVALID_PROCESSTRACE_HANDLE {
            stop_named();
            return Err(u32::MAX);
        }
        let worker = std::thread::Builder::new().name("optivance-fps".into()).spawn(move || {
            let mut handle = trace;
            // SAFETY: handle ouvert par OpenTraceW ; bloque jusqu'à CloseTrace / arrêt de la session.
            unsafe { ProcessTrace(&mut handle, 1, std::ptr::null_mut(), std::ptr::null_mut()) };
        });
        Ok(Self { session, trace, worker: worker.ok() })
    }

    pub fn stop(&mut self) {
        if self.session == 0 {
            return;
        }
        let name = wide(SESSION_NAME);
        let mut props = properties();
        // SAFETY: handles ouverts par start().
        unsafe {
            ControlTraceW(self.session, name.as_ptr(), props.as_mut_ptr().cast(), EVENT_TRACE_CONTROL_STOP);
            CloseTrace(self.trace);
        }
        self.session = 0;
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

impl Drop for FpsSession {
    fn drop(&mut self) {
        self.stop();
    }
}

#[link(name = "advapi32")]
unsafe extern "system" {
    fn StartTraceW(handle: *mut TraceHandle, name: *const u16, properties: *mut c_void) -> u32;
    fn ControlTraceW(handle: TraceHandle, name: *const u16, properties: *mut c_void, code: u32) -> u32;
    fn EnableTraceEx2(handle: TraceHandle, provider: *const c_void, code: u32, level: u8, any: u64, all: u64, timeout: u32, parameters: *mut c_void) -> u32;
    fn OpenTraceW(logfile: *mut c_void) -> TraceHandle;
    fn ProcessTrace(handles: *mut TraceHandle, count: u32, start: *mut c_void, end: *mut c_void) -> u32;
    fn CloseTrace(handle: TraceHandle) -> u32;
}

#[link(name = "kernel32")]
unsafe extern "system" {
    fn QueryPerformanceCounter(value: *mut i64) -> i32;
    fn QueryPerformanceFrequency(value: *mut i64) -> i32;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn guid_bytes_match_the_windows_layout() {
        // {CA11C036-0102-4A2D-A6AD-F03CFED5D3C9} en mémoire : Data1..3 petit-boutiste.
        assert_eq!(DXGI[..4], [0x36, 0xC0, 0x11, 0xCA]);
        assert_eq!(DXGI[4..8], [0x02, 0x01, 0x2D, 0x4A]);
        assert_eq!(DXGI[8..], [0xA6, 0xAD, 0xF0, 0x3C, 0xFE, 0xD5, 0xD3, 0xC9]);
    }

    #[test]
    fn properties_declare_size_and_name_offset() {
        let props = properties();
        let bytes: Vec<u8> = props.iter().flat_map(|w| w.to_le_bytes()).collect();
        let size = u32::from_le_bytes(bytes[0..4].try_into().unwrap()) as usize;
        assert_eq!(size, PROPERTIES_SIZE + (SESSION_NAME.len() + 1) * 2);
        assert_eq!(u32::from_le_bytes(bytes[116..120].try_into().unwrap()), 120);
        assert_eq!(u32::from_le_bytes(bytes[64..68].try_into().unwrap()), EVENT_TRACE_REAL_TIME_MODE);
    }
}
